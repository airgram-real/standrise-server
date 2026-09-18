using System;
using Axlebolt.RpcSupport.Protobuf;

namespace StandRiseServer.RpcServer.Core
{
    public static class RpcRequestExtension
    {
        public static T GetValue<T>(this RpcRequest request, int position)
        {
            Type type = typeof(T);
            return type.IsEnum 
                ? ((T)new EnumFromByteMethod(type).FromBytes(request.Params[position - 1])) 
                : ((T)new FromByteMethod(type).FromBytes(request.Params[position - 1]));
        }

        public static T GetValue<T>(this BinaryValue[] values, int position)
        {
            Type type = typeof(T);
            Type elementType = null;
            if (type.IsArray)
            {
                elementType = type.GetElementType();
            }
            return (((object)elementType != null && elementType.IsEnum) || type.IsEnum) 
                ? ((T)new EnumFromByteMethod(type).FromBytes(values[position])) 
                : ((T)new FromByteMethod(type).FromBytes(values[position]));
        }
    }
}
