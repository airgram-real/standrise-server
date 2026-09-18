using System;
using StandRiseServer.RpcServer.Api;
using StandRiseServer.RpcServer;
using Axlebolt.RpcSupport.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class NewsRemoteService : RpcClass
    {
        public NewsRemoteService(UserService user) : base(user)
        {
        }

        protected void GetNews(string guid)
        {
            _user.SendResponce(new ResponseMessage 
            { 
                RpcResponse = new RpcResponse 
                { 
                    Id = guid, 
                    Return = new BinaryValue() 
                } 
            });
        }

        public override void Invoke(RpcRequest request)
        {
            if (request.MethodName == "getNews") 
                GetNews(request.Id);
            else
                MethodNotFound(request);
        }
    }
}
