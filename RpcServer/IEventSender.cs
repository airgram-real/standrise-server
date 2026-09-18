using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
    public interface IEventSender
    {
        string eventListenerName { get; set; }
        void SendEvent(string eventName, object[] parameters);
    }
}
