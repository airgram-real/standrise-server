using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
    public class ServiceController
    {
		private readonly Type _serviceType;
		//private readonly Dictionary<MethodBase, RpcParameters> _rpcMethods = new Dictionary<MethodBase, RpcParameters>();
		public readonly UserService _user;
		public ServiceController(UserService client)
		{
			if (client == null)
			{
				throw new ArgumentNullException("client");
			}
			_serviceType = GetType();
			_user = client;
			ScanRpcMethods();
		}
		private void ScanRpcMethods()
		{
			RpcServiceAttribute attribute = AttributeUtils.GetAttribute<RpcServiceAttribute>(_serviceType);
			if (attribute == null)
			{
				Logger.Error(string.Concat(_serviceType, " is not rpc service"));
				throw new System.Exception("Incorrect rpc service type");
			}
			MethodInfo[] methods = _serviceType.GetMethods();
			foreach (MethodInfo methodInfo in methods)
			{
				RpcAttribute attribute2 = AttributeUtils.GetAttribute<RpcAttribute>(methodInfo);
				if (attribute2 != null)
				{
					Validate(methodInfo);
					Logger.Debug(methodInfo.Name + " registred");
					//_rpcMethods[methodInfo] = new RpcParameters(attribute.Name, attribute2.Name, ProtoReflectionUtils.GetParamToBytesMethods(methodInfo), ProtoReflectionUtils.GetReturnFromBytesMethod(methodInfo));
				}
				else
				{
					Logger.Debug(methodInfo.Name + " skipped");
				}
			}
		}
		private static void Validate(MethodInfo method)
		{
			ParameterInfo[] parameters = method.GetParameters();
			foreach (ParameterInfo parameterInfo in parameters)
			{
				if (parameterInfo.ParameterType != typeof(CancellationToken) && !ProtoReflectionUtils.IsSupportedType(parameterInfo.ParameterType))
				{
					throw new Exception("Invalid Rpc method argument type, " + method.Name + ". Unsupported type " + parameterInfo.ParameterType);
				}
			}
			if (!ProtoReflectionUtils.IsSupportedType(method.ReturnType))
			{
				throw new Exception("Invalid Rpc method return type, " + method.Name + ". Unsupported type " + method.ReturnType);
			}
		}
	}
	#region NeedTrash
	public class AttributeUtils
	{
		public static T GetAttribute<T>(Type type)
		{
			object[] customAttributes = type.GetCustomAttributes(typeof(T), inherit: true);
			if (customAttributes.Length != 0)
			{
				return (T)customAttributes[0];
			}
			Type[] interfaces = type.GetInterfaces();
			for (int i = 0; i < interfaces.Length; i++)
			{
				T attribute = GetAttribute<T>(interfaces[i]);
				if (Convert.ChangeType(attribute, typeof(T)) != null)
				{
					return attribute;
				}
			}
			return default(T);
		}

		public static T GetAttribute<T>(MethodInfo methodInfo)
		{
			object[] customAttributes = methodInfo.GetCustomAttributes(typeof(T), inherit: true);
			if (customAttributes.Length != 0)
			{
				return (T)customAttributes[0];
			}
			Type[] interfaces = methodInfo.ReflectedType.GetInterfaces();
			for (int i = 0; i < interfaces.Length; i++)
			{
				MethodInfo methodInfo2 = interfaces[i].GetMethod(types: (from param in methodInfo.GetParameters()
																		 select param.ParameterType).ToArray(), name: methodInfo.Name);
				if (methodInfo2 != null)
				{
					return GetAttribute<T>(methodInfo2);
				}
			}
			return default(T);
		}
	}
	#endregion
}
