using System;
using StandRiseServer.RpcServer.Api;
using StandRiseServer.RpcServer;
using Axlebolt.RpcSupport.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class SocialRemoteService : RpcClass
    {
        public SocialRemoteService(UserService user) : base(user)
        {
        }

        protected void GetSocial(string guid)
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
            if (request.MethodName == "getSocial") 
                GetSocial(request.Id);
            else
                MethodNotFound(request);
        }
    }
}
