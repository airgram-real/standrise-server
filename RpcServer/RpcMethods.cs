using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
	public class FromByteMethod
	{
		// Token: 0x0600020C RID: 524 RVA: 0x0000661C File Offset: 0x0000481C
		public FromByteMethod(Type parameterType)
		{
			this._parameterType = ProtoReflectionUtils.ToProtoType(parameterType);
			parameterType = this._parameterType;
			if (parameterType.IsArray)
			{
				parameterType = parameterType.GetElementType();
			}
			PropertyInfo property = parameterType.GetProperty("Parser");
			if (property == null)
			{
				throw new ArgumentException("Parse property not found in Type " + parameterType);
			}
			this._parser = (MessageParser)property.GetValue(null, null);
		}

		// Token: 0x0600020D RID: 525 RVA: 0x000039DD File Offset: 0x00001BDD
		public object FromBytes(BinaryValue bytes)
		{
			if (bytes.IsNull)
			{
				return null;
			}
			if (this._parameterType.IsArray)
			{
				return this.FromBytesArray(this._parameterType.GetElementType(), bytes);
			}
			return this.FromBytesOne(bytes.One, this._parameterType);
		}

		// Token: 0x0600020E RID: 526 RVA: 0x00006688 File Offset: 0x00004888
		protected virtual object FromBytesArray(Type type, BinaryValue binaryValue)
		{
			Array array = Array.CreateInstance(type, binaryValue.Array.Count);
			for (int i = 0; i < array.Length; i++)
			{
				object value = this.FromBytesOne(binaryValue.Array[i], type);
				array.SetValue(value, i);
			}
			return array;
		}

		// Token: 0x0600020F RID: 527 RVA: 0x00003A1B File Offset: 0x00001C1B
		protected virtual object FromBytesOne(ByteString bytes, Type elementType)
		{
			return ProtoReflectionUtils.ToOrigValue(this._parser.ParseFrom(bytes), elementType);
		}

		// Token: 0x0400009C RID: 156
		private readonly MessageParser _parser;

		// Token: 0x0400009D RID: 157
		private readonly Type _parameterType;
	}
	public class ProtoReflectionUtils
	{
		// Token: 0x06000220 RID: 544 RVA: 0x00003AAD File Offset: 0x00001CAD
		public static ToByteMethod[] GetParamToBytesMethods(MethodInfo method)
		{
			return (from parameterInfo in method.GetParameters()
					select ProtoReflectionUtils.CreateToByteMethod(parameterInfo.ParameterType)).ToArray<ToByteMethod>();
		}

		// Token: 0x06000221 RID: 545 RVA: 0x000066D8 File Offset: 0x000048D8
		public static ToByteMethod GetReturnToBytesMethod(MethodInfo method)
		{
			Type returnType = method.ReturnType;
			if (returnType == typeof(void))
			{
				return null;
			}
			return ProtoReflectionUtils.CreateToByteMethod(returnType);
		}

		// Token: 0x06000222 RID: 546 RVA: 0x00003ADE File Offset: 0x00001CDE
		public static ToByteMethod CreateToByteMethod(Type type)
		{
			if (ProtoReflectionUtils.IsEnumType(type))
			{
				return new EnumToByteMethod(type);
			}
			return new ToByteMethod(type);
		}

		// Token: 0x06000223 RID: 547 RVA: 0x00003AF5 File Offset: 0x00001CF5
		public static FromByteMethod[] GetParamFromBytesMethods(MethodInfo method)
		{
			return (from parameterInfo in method.GetParameters()
					select ProtoReflectionUtils.CreateFromByteMethod(parameterInfo.ParameterType)).ToArray<FromByteMethod>();
		}

		// Token: 0x06000224 RID: 548 RVA: 0x00006704 File Offset: 0x00004904
		public static FromByteMethod GetReturnFromBytesMethod(MethodInfo method)
		{
			Type returnType = method.ReturnType;
			if (returnType == typeof(void))
			{
				return null;
			}
			return ProtoReflectionUtils.CreateFromByteMethod(returnType);
		}

		// Token: 0x06000225 RID: 549 RVA: 0x00003B26 File Offset: 0x00001D26
		private static FromByteMethod CreateFromByteMethod(Type type)
		{
			if (ProtoReflectionUtils.IsEnumType(type))
			{
				return new EnumFromByteMethod(type);
			}
			return new FromByteMethod(type);
		}

		// Token: 0x06000226 RID: 550 RVA: 0x00003B3D File Offset: 0x00001D3D
		public static bool IsEnumType(Type type)
		{
			if (type.IsArray)
			{
				type = type.GetElementType();
			}
			return type.IsEnum;
		}

		// Token: 0x06000227 RID: 551 RVA: 0x00006730 File Offset: 0x00004930
		public static bool IsSupportedType(Type type)
		{
			if (type.IsArray)
			{
				type = type.GetElementType();
			}
			if (type == typeof(void))
			{
				return true;
			}
			if (type.IsEnum)
			{
				foreach (FieldInfo fieldInfo in type.GetFields())
				{
					if (!fieldInfo.Name.Equals("value__") && !Attribute.IsDefined(fieldInfo, typeof(OriginalNameAttribute)))
					{
						return false;
					}
				}
				return true;
			}
			return type == typeof(int) || type == typeof(int?) || (type.IsArray && type.GetElementType() == typeof(int)) || type == typeof(float) || type == typeof(float?) || (type.IsArray && type.GetElementType() == typeof(float)) || type == typeof(long) || type == typeof(long?) || (type.IsArray && type.GetElementType() == typeof(long)) || type == typeof(string) || (type.IsArray && type.GetElementType() == typeof(string)) || type == typeof(double) || type == typeof(double?) || (type.IsArray && type.GetElementType() == typeof(double)) || type == typeof(bool) || type == typeof(bool?) || (type.IsArray && type.GetElementType() == typeof(bool)) || type == typeof(byte) || type == typeof(byte?) || (type.IsArray && type.GetElementType() == typeof(byte)) || typeof(IMessage).IsAssignableFrom(type);
		}

		// Token: 0x06000228 RID: 552 RVA: 0x00006940 File Offset: 0x00004B40
		public static object ToProtoValue(object o)
		{
			// Проверка на null в самом начале
			if (o == null)
			{
				Logger.LogWarn("[ToProtoValue] Received null object, returning empty ByteString");
				return new Axlebolt.RpcSupport.Protobuf.Byte
				{
					Value = ByteString.Empty
				};
			}
			
			if (o.GetType().IsArray && o.GetType().GetElementType().IsEnum)
			{
				return o;
			}
			if (o is int)
			{
				return new Integer
				{
					Value = (int)o
				};
			}
			if (o is int[])
			{
				IntegerArray integerArray = new IntegerArray();
				integerArray.Value.AddRange((int[])o);
				return integerArray;
			}
			if (o is float)
			{
				return new Float
				{
					Value = (float)o
				};
			}
			if (o is float[])
			{
				FloatArray floatArray = new FloatArray();
				floatArray.Value.AddRange((float[])o);
				return floatArray;
			}
			if (o is long)
			{
				return new Long
				{
					Value = (long)o
				};
			}
			if (o is long[])
			{
				LongArray longArray = new LongArray();
				longArray.Value.AddRange((long[])o);
				return longArray;
			}
			if (o is string)
			{
				return new Axlebolt.RpcSupport.Protobuf.String
				{
					Value = (string)o
				};
			}
			if (o is string[])
			{
				StringArray stringArray = new StringArray();
				stringArray.Value.AddRange((string[])o);
				return stringArray;
			}
			if (o is double)
			{
				return new Axlebolt.RpcSupport.Protobuf.Double
				{
					Value = (double)o
				};
			}
			if (o is double[])
			{
				DoubleArray doubleArray = new DoubleArray();
				doubleArray.Value.AddRange((double[])o);
				return doubleArray;
			}
			if (o is bool)
			{
				return new Axlebolt.RpcSupport.Protobuf.Boolean
				{
					Value = (bool)o
				};
			}
			if (o is bool[])
			{
				BooleanArray booleanArray = new BooleanArray();
				booleanArray.Value.AddRange((bool[])o);
				return booleanArray;
			}
			if (o is byte)
			{
				return new Axlebolt.RpcSupport.Protobuf.Byte
				{
					Value = ByteString.CopyFrom(new byte[]
					{
						(byte)o
					})
				};
			}
			if (o is byte[])
			{
				byte[] bytes = (byte[])o;
				if (bytes == null || bytes.Length == 0)
				{
					return new Axlebolt.RpcSupport.Protobuf.Byte
					{
						Value = ByteString.Empty
					};
				}
				return new Axlebolt.RpcSupport.Protobuf.Byte
				{
					Value = ByteString.CopyFrom(bytes)
				};
			}
			return o;
		}

		// Token: 0x06000229 RID: 553 RVA: 0x00006B10 File Offset: 0x00004D10
		public static object ToOrigValue(object protoValue, Type origType)
		{
			if (protoValue is Integer)
			{
				return ((Integer)protoValue).Value;
			}
			if (protoValue is IntegerArray)
			{
				return ((IntegerArray)protoValue).Value.ToArray<int>();
			}
			if (protoValue is Float)
			{
				return ((Float)protoValue).Value;
			}
			if (protoValue is FloatArray)
			{
				return ((FloatArray)protoValue).Value.ToArray<float>();
			}
			if (protoValue is Long)
			{
				return ((Long)protoValue).Value;
			}
			if (protoValue is LongArray)
			{
				return ((LongArray)protoValue).Value.ToArray<long>();
			}
			if (protoValue is Axlebolt.RpcSupport.Protobuf.String)
			{
				return ((Axlebolt.RpcSupport.Protobuf.String)protoValue).Value;
			}
			if (protoValue is StringArray)
			{
				return ((StringArray)protoValue).Value.ToArray<string>();
			}
			if (protoValue is Axlebolt.RpcSupport.Protobuf.Double)
			{
				return ((Axlebolt.RpcSupport.Protobuf.Double)protoValue).Value;
			}
			if (protoValue is DoubleArray)
			{
				return ((DoubleArray)protoValue).Value.ToArray<double>();
			}
			if (protoValue is Axlebolt.RpcSupport.Protobuf.Boolean)
			{
				return ((Axlebolt.RpcSupport.Protobuf.Boolean)protoValue).Value;
			}
			if (protoValue is BooleanArray)
			{
				return ((BooleanArray)protoValue).Value.ToArray<bool>();
			}
			if (protoValue is Axlebolt.RpcSupport.Protobuf.Byte)
			{
				return ((Axlebolt.RpcSupport.Protobuf.Byte)protoValue).Value.ToByteArray()[0];
			}
			if (protoValue is ByteArray)
			{
				return ((ByteArray)protoValue).Value.ToByteArray();
			}
			return protoValue;
		}

		// Token: 0x0600022A RID: 554 RVA: 0x00006C80 File Offset: 0x00004E80
		public static Type ToProtoType(Type origType)
		{
			if (origType == typeof(int) || origType == typeof(int?))
			{
				return typeof(Integer);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(int))
			{
				return typeof(IntegerArray);
			}
			if (origType == typeof(float) || origType == typeof(float?))
			{
				return typeof(Float);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(float))
			{
				return typeof(FloatArray);
			}
			if (origType == typeof(long) || origType == typeof(long?))
			{
				return typeof(Long);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(long))
			{
				return typeof(LongArray);
			}
			if (origType == typeof(string))
			{
				return typeof(Axlebolt.RpcSupport.Protobuf.String);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(string))
			{
				return typeof(StringArray);
			}
			if (origType == typeof(double) || origType == typeof(double?))
			{
				return typeof(Axlebolt.RpcSupport.Protobuf.Double);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(double))
			{
				return typeof(DoubleArray);
			}
			if (origType == typeof(bool) || origType == typeof(bool?))
			{
				return typeof(Axlebolt.RpcSupport.Protobuf.Boolean);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(bool))
			{
				return typeof(BooleanArray);
			}
			if (origType == typeof(byte) || origType == typeof(byte?))
			{
				return typeof(Axlebolt.RpcSupport.Protobuf.Byte);
			}
			if (origType.IsArray && origType.GetElementType() == typeof(byte))
			{
				return typeof(ByteArray);
			}
			return origType;
		}
	}
	public class EnumFromByteMethod : FromByteMethod
	{
		// Token: 0x060001FB RID: 507 RVA: 0x00006290 File Offset: 0x00004490
		public EnumFromByteMethod(Type parameterType) : base((!parameterType.IsArray) ? typeof(Axlebolt.RpcSupport.Protobuf.Enum) : typeof(Axlebolt.RpcSupport.Protobuf.Enum[]))
		{
			if (parameterType == null)
			{
				throw new ArgumentNullException("parameterType");
			}
			this._enumType = parameterType;
			if (this._enumType.IsArray)
			{
				this._enumType = this._enumType.GetElementType();
			}
		}

		// Token: 0x060001FC RID: 508 RVA: 0x000038E0 File Offset: 0x00001AE0
		protected override object FromBytesArray(Type type, BinaryValue binaryValue)
		{
			return base.FromBytesArray(this._enumType, binaryValue);
		}

		// Token: 0x060001FD RID: 509 RVA: 0x000062F4 File Offset: 0x000044F4
		protected override object FromBytesOne(ByteString bytes, Type elementType)
		{
			Axlebolt.RpcSupport.Protobuf.Enum @enum = (Axlebolt.RpcSupport.Protobuf.Enum)base.FromBytesOne(bytes, elementType);
			return System.Enum.ToObject(this._enumType, @enum.Value);
		}

		// Token: 0x04000093 RID: 147
		private readonly Type _enumType;
	}
	public class ToByteMethod
	{
		// Token: 0x06000261 RID: 609 RVA: 0x00003D80 File Offset: 0x00001F80
		public ToByteMethod(Type parameterType)
		{
			this._parameterType = ProtoReflectionUtils.ToProtoType(parameterType);
		}

		// Token: 0x06000262 RID: 610 RVA: 0x00003D94 File Offset: 0x00001F94
		public virtual BinaryValue ToBytes(object arg)
		{
			if (arg == null)
			{
				return new BinaryValue
				{
					IsNull = true
				};
			}
			if (this._parameterType.IsArray)
			{
				return this.ToBytesArray(arg);
			}
			return new BinaryValue
			{
				IsNull = false,
				One = this.ToBytesOne(arg)
			};
		}

		// Token: 0x06000263 RID: 611 RVA: 0x00007638 File Offset: 0x00005838
		private BinaryValue ToBytesArray(object arg)
		{
			BinaryValue binaryValue = new BinaryValue
			{
				IsNull = false
			};
			IEnumerable enumerable = arg as IEnumerable;
			if (enumerable != null)
			{
				IEnumerator enumerator = enumerable.GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						object arg2 = enumerator.Current;
						binaryValue.Array.Add(this.ToBytesOne(arg2));
					}
				}
				finally
				{
					IDisposable disposable;
					if ((disposable = (enumerator as IDisposable)) != null)
					{
						disposable.Dispose();
					}
				}
			}
			return binaryValue;
		}

		// Token: 0x06000264 RID: 612 RVA: 0x00003DD4 File Offset: 0x00001FD4
		protected virtual ByteString ToBytesOne(object arg)
		{
			return ((IMessage)ProtoReflectionUtils.ToProtoValue(arg)).ToByteString();
		}

		// Token: 0x040000C3 RID: 195
		private readonly Type _parameterType;
	}
	public class EnumToByteMethod : ToByteMethod
	{
		// Token: 0x060001FE RID: 510 RVA: 0x000038EF File Offset: 0x00001AEF
		public EnumToByteMethod(Type parameterType) : base((!parameterType.IsArray) ? typeof(Axlebolt.RpcSupport.Protobuf.Enum) : typeof(Axlebolt.RpcSupport.Protobuf.Enum[]))
		{
		}

		// Token: 0x060001FF RID: 511 RVA: 0x00006320 File Offset: 0x00004520
		protected override ByteString ToBytesOne(object arg)
		{
			if (arg == null)
			{
				throw new ArgumentNullException("arg");
			}
			Axlebolt.RpcSupport.Protobuf.Enum arg2 = new Axlebolt.RpcSupport.Protobuf.Enum
			{
				Value = (int)arg
			};
			return base.ToBytesOne(arg2);
		}
	}
	public enum InventoryId
	{
		// Token: 0x04001331 RID: 4913
		None,
		// Token: 0x04001332 RID: 4914
		MedalAssistanceBronze = 100,
		// Token: 0x04001333 RID: 4915
		MedalAssistanceSilver,
		// Token: 0x04001334 RID: 4916
		MedalAssistanceGold,
		// Token: 0x04001335 RID: 4917
		MedalAssistancePlatinum,
		// Token: 0x04001336 RID: 4918
		MedalAssistanceBrilliant,
		// Token: 0x04001337 RID: 4919
		MedalVeteran2018Bronze,
		// Token: 0x04001338 RID: 4920
		MedalVeteran2018Silver,
		// Token: 0x04001339 RID: 4921
		MedalVeteran2018Gold,
		// Token: 0x0400133A RID: 4922
		MedalVeteran2018Platinum,
		// Token: 0x0400133B RID: 4923
		MedalVeteran2019Bronze,
		// Token: 0x0400133C RID: 4924
		MedalVeteran2019Silver,
		// Token: 0x0400133D RID: 4925
		MedalVeteran2019Gold,
		// Token: 0x0400133E RID: 4926
		MedalVeteran2019Platinum,
		// Token: 0x0400133F RID: 4927
		Medal2YearsSilver,
		// Token: 0x04001340 RID: 4928
		Medal2YearsGold,
		// Token: 0x04001341 RID: 4929
		OriginCase = 301,
		// Token: 0x04001342 RID: 4930
		FuriousCase,
		// Token: 0x04001343 RID: 4931
		RivalCase,
		// Token: 0x04001344 RID: 4932
		OriginBox = 401,
		// Token: 0x04001345 RID: 4933
		FuriousBox,
		// Token: 0x04001346 RID: 4934
		RivalBox,
		// Token: 0x04001347 RID: 4935
		GiftNewYear2019 = 501,
		// Token: 0x04001348 RID: 4936
		TwoYearsEventGoldPass = 601,
		// Token: 0x04001349 RID: 4937
		G22PixelCamouflage = 11001,
		// Token: 0x0400134A RID: 4938
		G22Nest,
		// Token: 0x0400134B RID: 4939
		G22Birds,
		// Token: 0x0400134C RID: 4940
		G22Casual,
		// Token: 0x0400134D RID: 4941
		G22Pattern,
		// Token: 0x0400134E RID: 4942
		G22Inferno,
		// Token: 0x0400134F RID: 4943
		G22FrostWyrm = 11008,
		// Token: 0x04001350 RID: 4944
		G22NestStatTrack = 1011002,
		// Token: 0x04001351 RID: 4945
		G22FrostWyrmStatTrack = 1011008,
		// Token: 0x04001352 RID: 4946
		USPGenesis = 12001,
		// Token: 0x04001353 RID: 4947
		USP_2Years,
		// Token: 0x04001354 RID: 4948
		USP_2YearsRed,
		// Token: 0x04001355 RID: 4949
		P350Cyber = 13001,
		// Token: 0x04001356 RID: 4950
		P350Savannah,
		// Token: 0x04001357 RID: 4951
		P350ForestSpirit,
		// Token: 0x04001358 RID: 4952
		P350Rally,
		// Token: 0x04001359 RID: 4953
		P350Skull,
		// Token: 0x0400135A RID: 4954
		P350CyberStatTrack = 1013001,
		// Token: 0x0400135B RID: 4955
		P350ForestSpiritStatTrack = 1013003,
		// Token: 0x0400135C RID: 4956
		P350RallyStatTrack,
		// Token: 0x0400135D RID: 4957
		UMP45Cyberpunk = 32001,
		// Token: 0x0400135E RID: 4958
		UMP45Pixel,
		// Token: 0x0400135F RID: 4959
		UMP45Shark,
		// Token: 0x04001360 RID: 4960
		UMP45Winged,
		// Token: 0x04001361 RID: 4961
		UMP45Beast,
		// Token: 0x04001362 RID: 4962
		UMP45Iron,
		// Token: 0x04001363 RID: 4963
		UMP45CyberpunkStatTrack = 1032001,
		// Token: 0x04001364 RID: 4964
		UMP45SharkStatTrack = 1032003,
		// Token: 0x04001365 RID: 4965
		UMP45WingedStatTrack,
		// Token: 0x04001366 RID: 4966
		UMP45BeastStatTrack,
		// Token: 0x04001367 RID: 4967
		MP7Offroad = 34001,
		// Token: 0x04001368 RID: 4968
		MP7Arcade,
		// Token: 0x04001369 RID: 4969
		MP7_2Years,
		// Token: 0x0400136A RID: 4970
		MP7_2YearsRed,
		// Token: 0x0400136B RID: 4971
		MP7OffroadStatTrack = 1034001,
		// Token: 0x0400136C RID: 4972
		MP7ArcadeStatTrack,
		// Token: 0x0400136D RID: 4973
		P90Radiation = 35001,
		// Token: 0x0400136E RID: 4974
		P90Ghoul,
		// Token: 0x0400136F RID: 4975
		P90Fury,
		// Token: 0x04001370 RID: 4976
		P90Pilot,
		// Token: 0x04001371 RID: 4977
		P90GhoulStatTrack = 1035002,
		// Token: 0x04001372 RID: 4978
		DeagleCaptainMorgan = 15001,
		// Token: 0x04001373 RID: 4979
		DeagleBlood,
		// Token: 0x04001374 RID: 4980
		DeaglePredator,
		// Token: 0x04001375 RID: 4981
		DeagleRedDragon,
		// Token: 0x04001376 RID: 4982
		DeagleWinner,
		// Token: 0x04001377 RID: 4983
		DeagleDragonGlass,
		// Token: 0x04001378 RID: 4984
		DeagleThunder,
		// Token: 0x04001379 RID: 4985
		DeaglePredatorStatTrack = 1015003,
		// Token: 0x0400137A RID: 4986
		DeagleRedDragonStatTrack,
		// Token: 0x0400137B RID: 4987
		DeagleDragonGlassStatTrack = 1015006,
		// Token: 0x0400137C RID: 4988
		AKRTreasureHunter = 44002,
		// Token: 0x0400137D RID: 4989
		AKRTiger,
		// Token: 0x0400137E RID: 4990
		AKRSport,
		// Token: 0x0400137F RID: 4991
		AKRNecromancer,
		// Token: 0x04001380 RID: 4992
		AKRCarbon,
		// Token: 0x04001381 RID: 4993
		AKR_2Years,
		// Token: 0x04001382 RID: 4994
		AKRTreasureHunterStatTrack = 1044002,
		// Token: 0x04001383 RID: 4995
		AKRSportStatTrack = 1044004,
		// Token: 0x04001384 RID: 4996
		AKRCarbonStatTrack = 1044006,
		// Token: 0x04001385 RID: 4997
		AKRNecromancerStatTrack = 1044005,
		// Token: 0x04001386 RID: 4998
		AKR12Railgun = 45001,
		// Token: 0x04001387 RID: 4999
		AKR12PixelCamouflage,
		// Token: 0x04001388 RID: 5000
		AKR12Mechanic,
		// Token: 0x04001389 RID: 5001
		AKR12Aurora,
		// Token: 0x0400138A RID: 5002
		AKR12RailgunStatTrack = 1045001,
		// Token: 0x0400138B RID: 5003
		AKR12PixelCamouflageStatTrack,
		// Token: 0x0400138C RID: 5004
		M4Predator = 46001,
		// Token: 0x0400138D RID: 5005
		M4Necromancer,
		// Token: 0x0400138E RID: 5006
		M4Tiger,
		// Token: 0x0400138F RID: 5007
		M4Evil,
		// Token: 0x04001390 RID: 5008
		M4Horseman,
		// Token: 0x04001391 RID: 5009
		M4Pro,
		// Token: 0x04001392 RID: 5010
		M4GrandPrix,
		// Token: 0x04001393 RID: 5011
		M4NecromancerStatTrack = 1046002,
		// Token: 0x04001394 RID: 5012
		M4ProStatTrack = 1046006,
		// Token: 0x04001395 RID: 5013
		M4GrandPrixStatTrack,
		// Token: 0x04001396 RID: 5014
		M16Camouflage = 47001,
		// Token: 0x04001397 RID: 5015
		M16Winged,
		// Token: 0x04001398 RID: 5016
		M16Facet,
		// Token: 0x04001399 RID: 5017
		M16WingedStatTrack = 1047002,
		// Token: 0x0400139A RID: 5018
		FamasBeagle = 48001,
		// Token: 0x0400139B RID: 5019
		FamasFury,
		// Token: 0x0400139C RID: 5020
		FamasHull,
		// Token: 0x0400139D RID: 5021
		FamasBeagleStatTrack = 1048001,
		// Token: 0x0400139E RID: 5022
		FamasFuryStatTrack,
		// Token: 0x0400139F RID: 5023
		FamasHullStatTrack,
		// Token: 0x040013A0 RID: 5024
		AWMSport = 51001,
		// Token: 0x040013A1 RID: 5025
		AWMPhoenix,
		// Token: 0x040013A2 RID: 5026
		AWMGear,
		// Token: 0x040013A3 RID: 5027
		AWMScratch,
		// Token: 0x040013A4 RID: 5028
		AWMDaemon,
		// Token: 0x040013A5 RID: 5029
		AWMSportV2,
		// Token: 0x040013A6 RID: 5030
		AWMGenesis,
		// Token: 0x040013A7 RID: 5031
		AWM_2YearsRed,
		// Token: 0x040013A8 RID: 5032
		AWMPhoenixStatTrack = 1051002,
		// Token: 0x040013A9 RID: 5033
		AWMGearStatTrack,
		// Token: 0x040013AA RID: 5034
		AWMScratchStatTrack,
		// Token: 0x040013AB RID: 5035
		AWMGenesisStatTrack = 1051007,
		// Token: 0x040013AC RID: 5036
		M40Quake = 52001,
		// Token: 0x040013AD RID: 5037
		M40Pro,
		// Token: 0x040013AE RID: 5038
		M40Beagle,
		// Token: 0x040013AF RID: 5039
		M40QuakeStatTrack = 1052001,
		// Token: 0x040013B0 RID: 5040
		M40BeagleStatTrack = 1052003,
		// Token: 0x040013B1 RID: 5041
		SM1014Facet = 62001,
		// Token: 0x040013B2 RID: 5042
		SM1014Pathfinder,
		// Token: 0x040013B3 RID: 5043
		SM1014Necromancer,
		// Token: 0x040013B4 RID: 5044
		SM1014NorthernCamouflage,
		// Token: 0x040013B5 RID: 5045
		SM1014Quake,
		// Token: 0x040013B6 RID: 5046
		SM1014Branches,
		// Token: 0x040013B7 RID: 5047
		SM1014PathfinderStatTrack = 1062002,
		// Token: 0x040013B8 RID: 5048
		SM1014NecromancerStatTrack,
		// Token: 0x040013B9 RID: 5049
		M9BayonetBlueBlood = 71001,
		// Token: 0x040013BA RID: 5050
		M9BayonetAncient,
		// Token: 0x040013BB RID: 5051
		M9BayonetScratch,
		// Token: 0x040013BC RID: 5052
		M9BayonetUniverse,
		// Token: 0x040013BD RID: 5053
		M9ByonetDragonGlass,
		// Token: 0x040013BE RID: 5054
		KarambitAcid = 72001,
		// Token: 0x040013BF RID: 5055
		KarambitClaw,
		// Token: 0x040013C0 RID: 5056
		KarambitGold,
		// Token: 0x040013C1 RID: 5057
		KarambitIceDragon,
		// Token: 0x040013C2 RID: 5058
		KarambitAncient,
		// Token: 0x040013C3 RID: 5059
		KarambitScratch,
		// Token: 0x040013C4 RID: 5060
		KarambitUniverse,
		// Token: 0x040013C5 RID: 5061
		jKommandoAncient = 73002,
		// Token: 0x040013C6 RID: 5062
		jKommandoReaper,
		// Token: 0x040013C7 RID: 5063
		jKommandoFloral,
		// Token: 0x040013C8 RID: 5064
		jKommandoLuxury = 73006,
		// Token: 0x040013C9 RID: 5065
		KnifeButterfly_Test = 75000,
		// Token: 0x040013CA RID: 5066
		Butterfly_DragonGlass,
		// Token: 0x040013CB RID: 5067
		Butterfly_Legacy,
		// Token: 0x040013CC RID: 5068
		Butterfly_Commpetive,

		Butterfly_BlackWidow,

		Butterfly_Starfall,

		AntiCamper = 202301,
		// Token: 0x04001412 RID: 5138
		Batrider,
		// Token: 0x04001413 RID: 5139
		BloodyClown,
		// Token: 0x04001414 RID: 5140
		Devilish,
		// Token: 0x04001415 RID: 5141
		Dracula,
		// Token: 0x04001416 RID: 5142
		EvilPumkin,
		// Token: 0x04001417 RID: 5143
		Feed,
		// Token: 0x04001418 RID: 5144
		GanstaPumkin,
		// Token: 0x04001419 RID: 5145
		Ghosty,
		// Token: 0x0400141A RID: 5146
		Ghoul,
		// Token: 0x0400141B RID: 5147
		GoldSkull,
		// Token: 0x0400141C RID: 5148
		InfernalSkull,
		// Token: 0x0400141D RID: 5149
		HurryGhost,
		// Token: 0x0400141E RID: 5150
		MadBat,
		// Token: 0x0400141F RID: 5151
		Mummy,
		// Token: 0x04001420 RID: 5152
		Punisher,
		// Token: 0x04001421 RID: 5153
		Rush,
		// Token: 0x04001422 RID: 5154
		S1001,
		// Token: 0x04001423 RID: 5155
		Snot,
		// Token: 0x04001424 RID: 5156
		Zombie,
		// Token: 0x04001425 RID: 5157
		Sticker_new1,
		// Token: 0x04001426 RID: 5158
		Sticker_new2,
		// Token: 0x04001427 RID: 5159
		Sticker_new3,
		// Token: 0x04001428 RID: 5160
		Sticker_new4,
		// Token: 0x04001429 RID: 5161
		Sticker_new5,
		// Token: 0x0400142A RID: 5162
		Sticker_new6,
		// Token: 0x0400142B RID: 5163
		Sticker_new7,
		// Token: 0x0400142C RID: 5164
		Sticker_new8,
		// Token: 0x0400142D RID: 5165
		Sticker_new9,
		// Token: 0x0400142E RID: 5166
		Sticker_new10,
		// Token: 0x0400142F RID: 5167
		Sticker_new11,
		// Token: 0x04001430 RID: 5168
		Sticker_new12
	}
}

