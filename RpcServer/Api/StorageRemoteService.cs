using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StandRiseServer.RpcServer.Api
{
    public class StorageRemoteService : RpcClass
    {
        public StorageRemoteService(UserService user) : base(user) { }

        private static ResponseMessage Error(string guid, int code)
        {
            return new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                    {
                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                        Code = code
                    }
                }
            };
        }

        private void SendUnauthorized(string guid)
        {
            _user.SendResponce(Error(guid, 401));
        }

        private static BinaryValue EmptyMessageReturn()
        {
            return new BinaryValue
            {
                IsNull = false,
                One = ByteString.Empty
            };
        }

        private static string ParseStringParam(BinaryValue value)
        {
            if (value == null || value.IsNull || value.One == null)
                return string.Empty;

            return (string)new FromByteMethod(typeof(string)).FromBytes(value);
        }

        private static byte[] ParseBytesParam(BinaryValue value)
        {
            if (value == null || value.IsNull || value.One == null)
                return Array.Empty<byte>();

            byte[] parsed = (byte[])new FromByteMethod(typeof(byte[])).FromBytes(value);
            return parsed ?? Array.Empty<byte>();
        }

        private static string ParseFilenameRequest(BinaryValue value)
        {
            return ParseStringParam(value);
        }

        private static void ParseWriteFileRequest(BinaryValue value, out string filename, out byte[] file)
        {
            filename = string.Empty;
            file = Array.Empty<byte>();

            if (value == null || value.IsNull || value.One == null)
                return;

            CodedInputStream input = new CodedInputStream(value.One.ToByteArray());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10:
                        filename = input.ReadString();
                        break;
                    case 18:
                        file = input.ReadBytes().ToByteArray();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }

        private FileStoragesDocument GetOrCreateFileStorage(ObjectId playerObjectId)
        {
            BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;

            try
            {
                FileStoragesDocument fileStorage = boltMain.GetFiles(playerObjectId);
                if (fileStorage != null)
                {
                    return fileStorage;
                }
            }
            catch
            {

            }

            try
            {
                boltMain.CreatePlayerFiles(playerObjectId.ToString());
            }
            catch
            {

            }

            return boltMain.GetFiles(playerObjectId);
        }

        private FileStoragesDocument EnsureBeansFile(ObjectId playerObjectId)
        {
            BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;
            FileStoragesDocument fileStorage = GetOrCreateFileStorage(playerObjectId);

            if (fileStorage == null || fileStorage.files == null || !fileStorage.files.Contains("beans"))
            {
                Logger.Log($"[Storage] Adding missing 'beans' file for player {playerObjectId}");
                boltMain.WriteOrCreateFile(playerObjectId, "beans", Array.Empty<byte>());
                fileStorage = GetOrCreateFileStorage(playerObjectId);
            }

            return fileStorage;
        }

        private static byte[] BsonValueToBytes(BsonValue fileBson)
        {
            if (fileBson == null || fileBson.IsBsonNull)
                return Array.Empty<byte>();

            if (fileBson.IsBsonArray)
            {
                return fileBson.AsBsonArray
                    .Select(x => (byte)Math.Max(0, Math.Min(255, x.ToInt32())))
                    .ToArray();
            }

            if (fileBson.IsBsonBinaryData)
                return fileBson.AsBsonBinaryData.Bytes ?? Array.Empty<byte>();

            return Array.Empty<byte>();
        }

        private static Storage[] ToStorageArray(FileStoragesDocument fileStorage)
        {
            if (fileStorage == null || fileStorage.files == null)
                return Array.Empty<Storage>();

            return fileStorage.files
                .Where(x => x != null && x.Name != null)
                .Select(x =>
                {
                    byte[] bytes = BsonValueToBytes(x.Value);
                    return new Storage
                    {
                        Filename = x.Name,
                        File = ByteString.CopyFrom(bytes)
                    };
                })
                .ToArray();
        }

        private static byte[] BuildReadFileResponseBytes(byte[] file)
        {
            file = file ?? Array.Empty<byte>();

            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                CodedOutputStream output = new CodedOutputStream(ms);
                if (file.Length != 0)
                {
                    output.WriteRawTag(10);
                    output.WriteBytes(ByteString.CopyFrom(file));
                }
                output.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] BuildReadAllFilesResponseBytes(IEnumerable<Storage> files)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                CodedOutputStream output = new CodedOutputStream(ms);
                foreach (Storage storage in files ?? Array.Empty<Storage>())
                {
                    output.WriteRawTag(10);
                    output.WriteMessage(storage);
                }
                output.Flush();
                return ms.ToArray();
            }
        }

        private void SendBinaryReturn(string guid, byte[] bytes)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(bytes ?? Array.Empty<byte>())
                    }
                }
            });
        }

        private void SendNullReturn(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        public void WriteFile(BinaryValue[] values, string guid, string methodName)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string id))
            {
                SendUnauthorized(guid);
                return;
            }

            ObjectId playerObjectId = ObjectId.Parse(id);
            string filename;
            byte[] file;
            bool wrapper019 = (values != null && values.Length == 1) || methodName.EndsWith("2", StringComparison.OrdinalIgnoreCase);

            if (wrapper019)
            {
                ParseWriteFileRequest(values[0], out filename, out file);
            }
            else
            {
                filename = values != null && values.Length > 0 ? ParseStringParam(values[0]) : string.Empty;
                file = values != null && values.Length > 1 ? ParseBytesParam(values[1]) : Array.Empty<byte>();
            }

            if (string.IsNullOrWhiteSpace(filename))
            {
                _user.SendResponce(Error(guid, 400));
                return;
            }

            EnsureBeansFile(playerObjectId);
            BoltGameDatabaseProvider.Instance.WriteOrCreateFile(playerObjectId, filename, file ?? Array.Empty<byte>());

            if (wrapper019)
                SendBinaryReturn(guid, Array.Empty<byte>());
            else
                SendNullReturn(guid);
        }

        public void ReadFiles(BinaryValue[] values, string guid, string methodName)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string id))
            {
                SendUnauthorized(guid);
                return;
            }

            ObjectId playerObjectId = ObjectId.Parse(id);
            FileStoragesDocument fileStorage = EnsureBeansFile(playerObjectId);
            Storage[] storages = ToStorageArray(fileStorage);

            bool wrapper019 = (values != null && values.Length == 1) || methodName.EndsWith("2", StringComparison.OrdinalIgnoreCase);
            if (wrapper019)
            {
                SendBinaryReturn(guid, BuildReadAllFilesResponseBytes(storages));
            }
            else
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(Storage[])).ToBytes(storages)
                    }
                });
            }
        }

        public void ReadFile(BinaryValue[] values, string guid)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string id))
            {
                SendUnauthorized(guid);
                return;
            }

            ObjectId playerObjectId = ObjectId.Parse(id);
            string filename = values != null && values.Length > 0 ? ParseFilenameRequest(values[0]) : string.Empty;

            if (string.IsNullOrWhiteSpace(filename))
            {
                filename = "beans";
            }

            // Сначала проверяем глобальное хранилище (картинки новостей и т.д.)
            byte[] globalFile = BoltMainDatabaseProvider.Instance.ReadGlobalFile(filename);
            if (globalFile != null)
            {
                SendBinaryReturn(guid, BuildReadFileResponseBytes(globalFile));
                return;
            }

            // Затем личное хранилище игрока
            FileStoragesDocument fileStorage = EnsureBeansFile(playerObjectId);
            byte[] file = Array.Empty<byte>();

            if (fileStorage != null && fileStorage.files != null && fileStorage.files.TryGetValue(filename, out BsonValue fileBson))
            {
                file = BsonValueToBytes(fileBson);
            }
            else
            {
                Logger.Log($"[Storage] File '{filename}' is missing for player {id}. Returning empty file.");
                BoltGameDatabaseProvider.Instance.WriteOrCreateFile(playerObjectId, filename, Array.Empty<byte>());
            }

            SendBinaryReturn(guid, BuildReadFileResponseBytes(file));
        }

        public void DeleteFile(BinaryValue[] values, string guid, string methodName)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string id))
            {
                SendUnauthorized(guid);
                return;
            }

            string filename = values != null && values.Length > 0 ? ParseFilenameRequest(values[0]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(filename))
            {
                BoltGameDatabaseProvider.Instance.DeleteFile(ObjectId.Parse(id), filename);
            }

            bool wrapper019 = (values != null && values.Length == 1) || methodName.EndsWith("2", StringComparison.OrdinalIgnoreCase);
            if (wrapper019)
                SendBinaryReturn(guid, Array.Empty<byte>());
            else
                SendNullReturn(guid);
        }

        public override void Invoke(RpcRequest request)
        {
            string methodName = request.MethodName ?? string.Empty;
            if (methodName.Equals("writeFile", StringComparison.OrdinalIgnoreCase) || methodName.Equals("writeFile2", StringComparison.OrdinalIgnoreCase)) WriteFile(request.Params.ToArray(), request.Id, methodName);
            else if (methodName.Equals("readAllFiles", StringComparison.OrdinalIgnoreCase) || methodName.Equals("readAllFiles2", StringComparison.OrdinalIgnoreCase) || methodName.Equals("readFiles", StringComparison.OrdinalIgnoreCase) || methodName.Equals("readFiles2", StringComparison.OrdinalIgnoreCase)) ReadFiles(request.Params.ToArray(), request.Id, methodName);
            else if (methodName.Equals("readFile", StringComparison.OrdinalIgnoreCase) || methodName.Equals("readFile2", StringComparison.OrdinalIgnoreCase)) ReadFile(request.Params.ToArray(), request.Id);
            else if (methodName.Equals("deleteFile", StringComparison.OrdinalIgnoreCase) || methodName.Equals("deleteFile2", StringComparison.OrdinalIgnoreCase)) DeleteFile(request.Params.ToArray(), request.Id, methodName);
            else MethodNotFound(request);
        }
    }
}
