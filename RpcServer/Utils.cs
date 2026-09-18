using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
    public class Utils
    {
		public static string MD5(string text)
		{
			return Utils.MD5_2(new UTF8Encoding().GetBytes(text));
		}
		public static string MD5_2(byte[] bytes)
		{
			byte[] array = new MD5CryptoServiceProvider().ComputeHash(bytes);
			string text = string.Empty;
			for (int i = 0; i < array.Length; i++)
			{
				text += Convert.ToString(array[i], 16).PadLeft(2, '0');
			}
			return text.PadLeft(32, '0');
		}
		public static byte[] Encrypt(string plainText, byte[] Key, byte[] IV)
		{
			byte[] encrypted;
			// Create a new AesManaged.    
			using (AesManaged aes = new AesManaged())
			{
				// Create encryptor    
				ICryptoTransform encryptor = aes.CreateEncryptor(Key, IV);
				// Create MemoryStream    
				using (MemoryStream ms = new MemoryStream())
				{
					// Create crypto stream using the CryptoStream class. This class is the key to encryption    
					// and encrypts and decrypts data from any given stream. In this case, we will pass a memory stream    
					// to encrypt    
					using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
					{
						// Create StreamWriter and write data to a stream    
						using (StreamWriter sw = new StreamWriter(cs))
							sw.Write(plainText);
						encrypted = ms.ToArray();
					}
				}
			}
			// Return encrypted data    
			return encrypted;
		}
		public static byte[] EncryptByte(byte[] plainText, byte[] Key, byte[] IV)

		{

			byte[] encrypted;
			// Create a new AesManaged.    
			using (AesManaged aes = new AesManaged())
			{
				// Create encryptor    
				ICryptoTransform encryptor = aes.CreateEncryptor(Key, IV);
				// Create MemoryStream    
				using (MemoryStream ms = new MemoryStream())
				{
					// Create crypto stream using the CryptoStream class. This class is the key to encryption    
					// and encrypts and decrypts data from any given stream. In this case, we will pass a memory stream    
					// to encrypt    
					using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
					{
						// Create StreamWriter and write data to a stream    

						cs.Write(plainText, 0, plainText.Length);
						cs.FlushFinalBlock();
						encrypted = ms.ToArray();
					}
				}
			}
			// Return encrypted data    
			return encrypted;
		}
		public static string Decrypt(byte[] cipherText, byte[] Key, byte[] IV)
		{
			string plaintext = null;
			// Create AesManaged    
			using (AesManaged aes = new AesManaged())
			{
				// Create a decryptor    
				ICryptoTransform decryptor = aes.CreateDecryptor(Key, IV);
				// Create the streams used for decryption.    
				using (MemoryStream ms = new MemoryStream(cipherText))
				{
					// Create crypto stream    
					using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
					{
						// Read crypto stream    
						using (StreamReader reader = new StreamReader(cs))
							plaintext = reader.ReadToEnd();
					}
				}
			}
			return plaintext;
		}
		public static byte[] DecryptByte(byte[] cipherText, byte[] Key, byte[] IV)
		{
			byte[] plaintext = null;
			// Create AesManaged    
			using (AesManaged aes = new AesManaged())
			{
				// Create a decryptor    
				ICryptoTransform decryptor = aes.CreateDecryptor(Key, IV);
				// Create the streams used for decryption.    
				using (MemoryStream ms = new MemoryStream())
				{
					// Create crypto stream    
					using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
					{
						cs.Write(cipherText, 0, cipherText.Length);
						cs.FlushFinalBlock();
						plaintext = ms.ToArray();
					}
				}
			}
			return plaintext;
		}
		public static async Task<byte[]> DecryptByteAsync(byte[] cipherText, byte[] Key, byte[] IV)
		{
			byte[] plaintext = null;
			// Create AesManaged    
			using (AesManaged aes = new AesManaged())
			{
				// Create a decryptor    
				ICryptoTransform decryptor = aes.CreateDecryptor(Key, IV);
				// Create the streams used for decryption.    
				using (MemoryStream ms = new MemoryStream())
				{
					// Create crypto stream    
					using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
					{
						await cs.WriteAsync(cipherText, 0, cipherText.Length);
						cs.FlushFinalBlock();
						plaintext = ms.ToArray();
					}
				}
			}
			return plaintext;
		}
		public static long ToUnixTime(DateTime date)
		{
			DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			return (long)(date.ToUniversalTime() - dateTime).TotalMilliseconds;
		}

		public static DateTime FromUnixTime(long unixTime)
		{
			DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			return dateTime.AddMilliseconds(unixTime);
		}

		public static byte[] ExtractBinaryValueOne(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return bytes;
			}
			try
			{
				int index = 0;
				while (index < bytes.Length)
				{
					uint tag = Utils.ReadVarint32(bytes, ref index);
					int fieldNumber = (int)(tag >> 3);
					int wireType = (int)(tag & 7U);
					if ((fieldNumber == 1 || fieldNumber == 3) && wireType == 2)
					{
						uint length = Utils.ReadVarint32(bytes, ref index);
						if ((long)index + (long)((ulong)length) <= (long)bytes.Length)
						{
							byte[] nested = new byte[length];
							Array.Copy(bytes, index, nested, 0, (int)length);
							return nested;
						}
					}
					if (wireType == 0)
					{
						Utils.ReadVarint32(bytes, ref index);
					}
					else if (wireType == 2)
					{
						uint len = Utils.ReadVarint32(bytes, ref index);
						index += (int)len;
					}
					else if (wireType == 1)
					{
						index += 8;
					}
					else if (wireType == 5)
					{
						index += 4;
					}
					else
					{
						break;
					}
				}
			}
			catch
			{
			}
			return bytes;
		}

		public static uint ReadVarint32(byte[] bytes, ref int index)
		{
			uint result = 0U;
			int shift = 0;
			while (index < bytes.Length)
			{
				byte b = bytes[index++];
				result |= (uint)(b & 127) << shift;
				if ((b & 128) == 0)
				{
					break;
				}
				shift += 7;
			}
			return result;
		}
	}
}
