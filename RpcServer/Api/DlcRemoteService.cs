using Axlebolt.RpcSupport.Protobuf;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("DlcRemoteService")]
    public class DlcRemoteService : RpcClass
    {
        public DlcRemoteService(UserService user) : base(user)
        {
        }

        public async Task GetDlcAsync(RpcRequest request)
        {
            // Поскольку у нас нет Protobuf-схемы для DlcResponse в RpcServer, 
            // отправка JSON-строки ломает парсер клиента (он пытается читать JSON как байты Protobuf-сообщения).
            // Поэтому возвращаем пустой корректный ответ, как это было в оригинальной заглушке,
            // чтобы клиент не падал с ошибкой Mismatched end-group tag.
            StubHelper.SendEmptyResponse(_user, request.Id);
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            switch (methodName)
            {
                case "dlc":
                case "dlc2":
                case "dlccompatible":
                case "dlccompatible2":
                case "getallreleaseddlc":
                case "getallreleaseddlc2":
                case "getalldlc":
                case "getalldlc2":
                case "getcurrentreleaseddlc":
                case "getcurrentreleaseddlc2":
                case "getreleaseddlc":
                case "getreleaseddlc2":
                case "getcompatiblereleaseddlc":
                case "getcompatiblereleaseddlc2":
                    await GetDlcAsync(request);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }

        public override void Invoke(RpcRequest request)
        {
            InvokeAsync(request).Wait();
        }
    }
}
