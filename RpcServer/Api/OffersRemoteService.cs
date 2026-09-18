using System;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    public class OffersRemoteService : RpcClass
    {
        public OffersRemoteService(UserService user) : base(user)
        {
        }

        /// <summary>
        /// Пустой ответ — безопасен для входа (минимальный оффер без price/items → NullReference на клиенте).
        /// Таймер HALLOWEEN SPINS — из GameEvent.DateUntil (getPlayerCurrentGameEvents).
        /// </summary>
        protected void GetSpecialOffers(string guid)
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

        public override void Invoke(RpcRequest request)
        {
            string method = (request.MethodName ?? "").ToLowerInvariant();
            if (method == "getspecialoffers")
                GetSpecialOffers(request.Id);
            else
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = request.Id,
                        Return = new BinaryValue { IsNull = true }
                    }
                });
            }
        }
    }
}
