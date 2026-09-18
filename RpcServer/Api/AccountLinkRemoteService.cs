using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using MongoDB.Bson;
using StandRiseServer.RpcServer.Helpers;

namespace StandRiseServer.RpcServer.Api
{
    public class AccountLinkRemoteService : RpcClass
    {
        public AccountLinkRemoteService(UserService user) : base(user)
        {
        }

        public async Task GetLinkedAuth(RpcRequest request)
        {
            string playerId = PlayerId;
            if (string.IsNullOrEmpty(playerId))
            {
                SendError(request.Id, 401); // Unauthorized
                return;
            }

            var response = new GetLinkedAuthResponse();
            BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;

            // Check Google Play
            try
            {
                var googleAccount = boltMain.GetGoogleAccountByPlayerId(playerId);
                if (googleAccount != null)
                {
                    response.AuthTypes.Add(new LinkedAuth
                    {
                        AuthType = AuthType.GooglePlay,
                        Primary = true 
                    });
                }
            }
            catch (AccountNotFoundException) { }
            catch (System.Exception ex)
            {
                Logger.Error($"[AccountLink] Error getting Google account for {playerId}: {ex.Message}");
            }

            // Check Test Account
            try
            {
                var testAccount = boltMain.GetTestAccountByPlayerId(playerId);
                if (testAccount != null)
                {
                    response.AuthTypes.Add(new LinkedAuth
                    {
                        AuthType = AuthType.Test,
                        Primary = response.AuthTypes.Count == 0 
                    });
                }
            }
            catch (AccountNotFoundException) { }
            catch (System.Exception ex)
            {
                Logger.Error($"[AccountLink] Error getting Test account for {playerId}: {ex.Message}");
            }

            SendResponse(request.Id, response);
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            if (methodName == "getlinkedauth" || methodName == "144")
            {
                await GetLinkedAuth(request);
            }
            else
            {
                MethodNotFound(request);
            }
        }

        public override void Invoke(RpcRequest request)
        {
            InvokeAsync(request).Wait();
        }
    }
}
