using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using System;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("PromoRemoteService")]
    public class PromoRemoteService : RpcClass
    {
        public PromoRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            switch (methodName)
            {
                case "getpromos":
                case "getpromotions":
                case "getactivepromos":
                case "getpromos2":
                    SendEmptyArrayResponse(request.Id);
                    break;
                case "getbanner":
                case "getbanners":
                    SendEmptyArrayResponse(request.Id);
                    break;
                case "getbattlepasspromo":
                case "getbattlepasspromos":
                    SendEmptyArrayResponse(request.Id);
                    break;
                default:
                    SendEmptyArrayResponse(request.Id);
                    break;
            }
        }

        private void SendEmptyArrayResponse(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new ToByteMethod(typeof(object[])).ToBytes(Array.Empty<object>())
                }
            });
        }
    }
}
