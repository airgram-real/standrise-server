using System;

namespace StandRiseServer.RpcServer
{
	[AttributeUsage(AttributeTargets.Class, Inherited = true)]
	public class RpcServiceAttribute : Attribute
	{
		public readonly string Name;

		public RpcServiceAttribute(string name)
		{
			Name = name;
		}
	}
}
