using ServerLib.Config;
using System;
using System.IO;
using System.Text;


namespace CoreLib {
 public sealed class GUIDFactory {
   static public GUIDFactory INSTANCE = new GUIDFactory();

   // start time 2015 8 8 0h 0m 0s
   static private readonly DateTime StandardTime = new DateTime(2015, 8, 8, 0, 0, 0);
   static private readonly UInt32 TimePeriod = 10;
   static private readonly Int64 TimeGrain = TimeSpan.TicksPerMinute;
   static private readonly Int64 TimeStampInterval = TimePeriod * TimeGrain;
   static private readonly String ChunkFileName = "chunk.txt";

   static private readonly UInt32 MAX_CHUNK_NUM = 0xFFE;
   static private readonly UInt32 MAX_INSTANCE_NUM = 0xFFFE;
   static private readonly UInt32 CHUNK_OFFSET = 0x00000FFF;
   static private readonly UInt32 TIME_OFFSET = 0xFFFFF000;

   private string chunk_file_full_name = string.Empty;

   // 20 bit time key, 12 bit chunk
   public UInt32 m_time_stamp = 0;
   private UInt16 m_owner_id = 0;
   private UInt16 m_instance_id = 0;
   private Int64 m_elapsedTick = 0;
   private Boolean m_isUpdate = false;
   private Object m_factory_sync = new Object();

   public ushort OwnerID { get { return m_owner_id; } }

   public void Start(ushort owner_server_id) {
     try {
       UInt32 current_time_key = 0;
       TimeSpan elapsedTime = DateTime.Now - GUIDFactory.StandardTime;
       current_time_key = (UInt32)(elapsedTime.Ticks / TimeStampInterval);

       var builder = new StringBuilder(ConfigManager.PATH_ITEM_CHUNK);
       builder.Append(GUIDFactory.ChunkFileName);
       chunk_file_full_name = builder.ToString();
        

       UInt32 time_stamp = 0;
       UInt16 lastChunkNum = 0;
       bool isChunkRead = this.LoadTimeKeyAndChunkNum(current_time_key, out time_stamp, out lastChunkNum, chunk_file_full_name);
       if (isChunkRead == false)
         throw new InvalidOperationException("Get Chunk Num Fail");

       // max chunk num 보다 큰 경우(청크 넘침) time key 값을 증가해서 키를 만든다.
       if (lastChunkNum > MAX_CHUNK_NUM) {
         lastChunkNum = 0;
         ++current_time_key;
       }

       // add chunk
       time_stamp |= (UInt32)(lastChunkNum & CHUNK_OFFSET);

       // add time key
       time_stamp |= (current_time_key << 12);

       // 언제나 처음 서버가 뜰 때는 청크번호를 1 증가 시켜준다(중복 방지를 위해)
       ++time_stamp;

       this.m_time_stamp = time_stamp;
       this.m_owner_id = owner_server_id;
       this.m_instance_id = 0;

       this.m_time_stamp = time_stamp;
       this.m_isUpdate = true;

       this.SaveTimeKeyAndChunkNum(time_stamp);

       GameLog.Log( CommonLib.Interface.LogLevel.Info, "Start GUID TimeStamp {0:x} : {1} owner {2}", this.m_time_stamp, this.m_time_stamp, owner_server_id);
     }
     catch (System.Exception ex) {
       this.m_isUpdate = false;
       GameLog.Log(CommonLib.Interface.LogLevel.Error, ex);
     }
   }

   public void Update(Int64 elapsedTime) {
     if (this.m_isUpdate == false)
       return;

     this.m_elapsedTick += elapsedTime;
     if (this.m_elapsedTick > GUIDFactory.TimeStampInterval) {
       TimeSpan spawn = DateTime.Now - GUIDFactory.StandardTime;
       UInt32 current_time_key = (UInt32)(spawn.Ticks / TimeStampInterval) << 12;

       UInt32 older_time_key = (this.m_time_stamp & TIME_OFFSET);
       if (current_time_key > older_time_key) {
         this.m_time_stamp = current_time_key;
         this.m_instance_id = 0;

         //Logger.InfoLog("Time elapsed Update older {0:x} now {1:x}", older_time_key, current_time_key);
       }
       //Logger.DebugLog("Update Time Key {0:x}", this.m_time_stamp);
       this.m_elapsedTick = 0;
     }
   }

   private bool CreateChunkFile(UInt32 current_time_key, String fileName) {
     // Create the writer for data.
     FileStream fs = null;
     try {
       fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);

       using (var sw = new StreamWriter(fs)) {
         UInt32 first_time_key_and_chunk = current_time_key << 12;
         sw.WriteLine(first_time_key_and_chunk);
       }
     }
     finally {
       if (fs != null)
         fs.Dispose();
     }

     return true;
   }

   private bool LoadTimeKeyAndChunkNum(UInt32 current_time_key, out UInt32 time_stamp, out UInt16 lastChunkNum, string chunk_file_full_name) {
     time_stamp = 0;
     lastChunkNum = 0;
     try {

       if (!File.Exists(chunk_file_full_name)) {
         bool isCrate = this.CreateChunkFile(current_time_key, chunk_file_full_name);
         if (isCrate == false)
           throw new InvalidOperationException("New Chunk File Cretae Fail");
       }

       // 저장된 시간값이 현재 시간 값보다 크다면, 저장된 시간값을 사용
       // 현재 시간값이 저장된 시간값 보다 크다면 현재 값을 사용해서 키를 생성한다.
       using (StreamReader sr = File.OpenText(chunk_file_full_name)) {
         String input = String.Empty;
         while ((input = sr.ReadLine()) != null) {
           UInt32 saved_time_stamp = Convert.ToUInt32(input);
           UInt32 current_key = current_time_key << 12;
           UInt32 saved_time_key = (saved_time_stamp & TIME_OFFSET);
           if (current_key > saved_time_key) {
             // chunk is zero, and setting time stamp
             time_stamp = current_key;
           }
           else {
             // set is saved chunk stamp
             time_stamp = saved_time_stamp;
           }

           lastChunkNum = (UInt16)(this.m_time_stamp & CHUNK_OFFSET);
           GameLog.Log( CommonLib.Interface.LogLevel.Info, "Loaded TimeStamp = {0:x} Chunk ID {1:x}", time_stamp, lastChunkNum);
         }
       }
       return true;
     }
     catch (System.Exception ex) {
       GameLog.Log(CommonLib.Interface.LogLevel.Error, ex);
       return false;
     }
   }

   private void SaveTimeKeyAndChunkNum(UInt32 time_stamp) {
     // Create the writer for data.
     FileStream fs = null;
     try {
       fs = new FileStream(chunk_file_full_name, FileMode.Create, FileAccess.Write);

       using (var sw = new StreamWriter(fs)) {
         sw.WriteLine(time_stamp);
       }
     }
     finally {
       if (fs != null)
         fs.Dispose();
     }
   }

   public UInt64 GenerateKey(ushort select_id = 0) {
     lock (m_factory_sync) {
       ulong unique_key = 0;
       bool isFullInstanceID = false;
       bool isFullChunk = false;

       uint time_stamp = ++this.m_time_stamp;
       ushort instance_id = ++this.m_instance_id;
       ushort owner_id = this.m_owner_id;


       // 인스턴스 아이디가 가득 차면 청크 번호 증가, 인스턴스 아이디는 초기화
       if (instance_id > MAX_INSTANCE_NUM)
         isFullInstanceID = true;

       // 청크 번호가 가득차면 시간 꼬리표 증가.
       if (isFullInstanceID) {
         ++time_stamp;
         UInt16 current_chunk_num = (UInt16)(this.m_time_stamp & CHUNK_OFFSET);
         if (current_chunk_num > MAX_CHUNK_NUM)
           isFullChunk = true;
         else
           SaveTimeKeyAndChunkNum(time_stamp);

         instance_id = 0;
         //Logger.InfoLog("Update Chunk {0} time {1}", current_chunk_num, this.m_time_stamp);
       }

       if (isFullChunk) {
         // shift로 인한 chunk num은 0으로 setting
         UInt32 current_time_key = (time_stamp & TIME_OFFSET) >> 12;
         ++current_time_key;
         time_stamp = current_time_key << 12;
         SaveTimeKeyAndChunkNum(time_stamp);

         //Logger.InfoLog("Chunk Full Update TimeKey {0:x} make count {1}", this.m_time_stamp, this.m_current_count);
       }

       // 시간 꼬리표가 가득차면 게임 접어!!
       this.m_time_stamp = time_stamp;
       this.m_instance_id = instance_id;

       unique_key = time_stamp;
       unique_key = unique_key << 32;

       unique_key |= instance_id;

       if (select_id > 0)
         owner_id = select_id;

       unique_key |= (UInt32)(owner_id << 16);

       return unique_key;
     }
   }
 }
}
