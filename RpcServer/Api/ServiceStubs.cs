using System;
using System.Threading.Tasks;
using Axlebolt.RpcSupport.Protobuf;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class StubHelper
    {
        public static void SendEmptyResponse(UserService user, string guid)
        {
            user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.Empty
                    }
                }
            });
        }
    }

    [RpcService("AchievementRemoteService")]
    public class AchievementRemoteService : RpcClass
    {
        public AchievementRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] AchievementRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }

    [RpcService("CrashLogRemoteService")]
    public class CrashLogRemoteService : RpcClass
    {
        public CrashLogRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] CrashLogRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }


    [RpcService("GameServerGameEventRemoteService")]
    public class GameServerGameEventRemoteService : RpcClass
    {
        public GameServerGameEventRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            _ = InvokeAsync2(request);
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            await InvokeAsync2(request);
        }

        private async Task InvokeAsync2(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            try
            {
                switch (methodName)
                {
                    // Отдаём авторитетный список активных игровых событий (БП) - клиент
                    // использует его, чтобы определить, активен ли Battle Pass и можно ли
                    // открыть плитку в главном меню. Раньше это был заглушка с пустым ответом,
                    // из-за чего клиент не видел активных событий и плитка БП не открывалась.
                    case "getcurrentgameeventsbyserver":
                        {
                            string eventId = string.Empty;
                            if (request.Params != null)
                            {
                                eventId = GameEventRemoteService.ReadByServerEventId(request.Params.ToArray());
                            }
                            var response = await GameEventRemoteService.BuildByServerGameEvents(_user, eventId);
                            if (response != null)
                            {
                                SendGameEventResponse(request.Id, response);
                            }
                            else
                            {
                                SendError(request.Id, 401);
                            }
                            return;
                        }
                    case "getallchallengesbyserver":
                    case "progressgameeventbyserver":
                    case "progressgameeventsbyserver":
                    case "processchallengebyserver":
                    case "setchallengeprogressesbyserver":
                    default:
                        Console.WriteLine($"[STUB] GameServerGameEventRemoteService.{request.MethodName} called");
                        StubHelper.SendEmptyResponse(_user, request.Id);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(request.Id, 500);
            }
        }

        private void SendGameEventResponse(string guid, Axlebolt.Bolt.Protobuf.GetCurrentGameEventsResponse response)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { One = response.ToByteString() }
                }
            });
        }
    }

    [RpcService("GameServerInventoryRemoteService")]
    public class GameServerInventoryRemoteService : RpcClass
    {
        public GameServerInventoryRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] GameServerInventoryRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }


    [RpcService("GroupRemoteService")]
    public class GroupRemoteService : RpcClass
    {
        public GroupRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] GroupRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }

    [RpcService("AppleIdAuthRemoteService")]
    public class AppleIdAuthRemoteService : RpcClass
    {
        public AppleIdAuthRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] AppleIdAuthRemoteService.{request.MethodName} called");
            SendError(request.Id, 401);
        }
    }

    [RpcService("HuaweiAuthRemoteService")]
    public class HuaweiAuthRemoteService : RpcClass
    {
        public HuaweiAuthRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] HuaweiAuthRemoteService.{request.MethodName} called");
            SendError(request.Id, 401);
        }
    }

    [RpcService("FacebookAuthRemoteService")]
    public class FacebookAuthRemoteService : RpcClass
    {
        public FacebookAuthRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] FacebookAuthRemoteService.{request.MethodName} called");
            SendError(request.Id, 401);
        }
    }

    [RpcService("GameCenterAuthRemoteService")]
    public class GameCenterAuthRemoteService : RpcClass
    {
        public GameCenterAuthRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] GameCenterAuthRemoteService.{request.MethodName} called");
            SendError(request.Id, 401);
        }
    }

    [RpcService("XiaomiAuthRemoteService")]
    public class XiaomiAuthRemoteService : RpcClass
    {
        public XiaomiAuthRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] XiaomiAuthRemoteService.{request.MethodName} called");
            SendError(request.Id, 401);
        }
    }

    [RpcService("SystemMessagesRemoteService")]
    public class SystemMessagesRemoteService : RpcClass
    {
        public SystemMessagesRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] SystemMessagesRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }

    [RpcService("TournamentsRemoteService")]
    public class TournamentsRemoteService : RpcClass
    {
        public TournamentsRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] TournamentsRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }

    [RpcService("UgcRemoteService")]
    public class UgcRemoteService : RpcClass
    {
        public UgcRemoteService(UserService user) : base(user) { }
        public override void Invoke(RpcRequest request)
        {
            Console.WriteLine($"[STUB] UgcRemoteService.{request.MethodName} called");
            StubHelper.SendEmptyResponse(_user, request.Id);
        }
    }
}
