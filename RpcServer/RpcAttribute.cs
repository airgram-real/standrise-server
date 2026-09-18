using System;

namespace StandRiseServer.RpcServer
{
	[AttributeUsage(AttributeTargets.Method)]
	public class RpcAttribute : Attribute
	{
		public readonly string Name;

		public RpcAttribute(string name)
		{
			Name = name;
		}
	}
}
