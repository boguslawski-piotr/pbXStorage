﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using pbXNet;

namespace Test.NETStd
{
	class Program
	{
		static readonly char[] commaCharArray = { ',' };

		static HttpClient http = new HttpClient();
		static Uri ApiUri = new Uri("http://localhost:50768/api/storage/");

		// generated in app, should be stored in a safe way
		static IAsymmetricCryptographerKeyPair appKeys;

		// given from pbXStorage registration tool/web site
		static string clientId;
		static IAsymmetricCryptographerKeyPair clientPblKey;

		// given from server during communication
		static string appToken;

		static string storageToken;
		static IAsymmetricCryptographerKeyPair storagePblKey;

		static RsaCryptographer cryptographer;

		static async Task InitializeAsync()
		{
			cryptographer = new RsaCryptographer();
			appKeys = cryptographer.GenerateKeyPair();

			http.Timeout = TimeSpan.FromSeconds(30);
		}

		static async Task<string> ExecuteCommandAsync(string cmd, Uri uri, HttpContent content = null)
		{
			try
			{
				Console.WriteLine($"REQUEST: {cmd}: {uri}");
				//Log.I($"REQUEST: {cmd}: {uri}", this);

				HttpResponseMessage response = null;
				switch (cmd)
				{
					case "GET":
						response = await http.GetAsync(uri);
						break;
					case "POST":
						response = await http.PostAsync(uri, content);
						break;
					case "PUT":
						response = await http.PutAsync(uri, content);
						break;
					case "DELETE":
						response = await http.DeleteAsync(uri);
						break;
				}

				if (response != null)
				{
					Console.Write("RESPONSE: ");

					if (response.IsSuccessStatusCode)
					{
						var responseContent = await response.Content.ReadAsStringAsync();
						responseContent = Obfuscator.DeObfuscate(responseContent);

						Console.WriteLine($"{responseContent}");

						string[] contentData = responseContent.Split(commaCharArray, 2);
						if (contentData[0] == "ERROR")
						{
							throw new StorageOnPbXStorageException(contentData[1]);
						}

						return contentData.Length > 1 ? contentData[1] : null;
					}
					else
						throw new StorageOnPbXStorageException($"Failed to read data. Error: {response.StatusCode}.");
				}
				else
					throw new StorageOnPbXStorageException($"Command {cmd} unrecognized.");
			}
			catch (Exception ex)
			{
				string message = $"{ex.Message}";
				if (ex.InnerException != null)
					message += $"\n{ex.InnerException.Message + (ex.InnerException.Message.EndsWith(".") ? "" : ".")}";
#if DEBUG
				message += $"\n{cmd}: {uri}";
#endif
				throw new StorageOnPbXStorageException(message);
			}
		}

		static async Task NewClientTestAsync()
		{
			string httpcmd = "GET";
			string cmd = "newclient";

			Uri uri = new Uri(ApiUri, cmd);

			//string response = await ExecuteCommandAsync(httpcmd, uri);

			//string[] clientData = response.Split(commaCharArray, 2);
			//clientId = clientData[0];
			//clientPblKey = new RsaKeyPair(null, clientData[1]);

			clientId = "a2b3dbe294614443b87c01e138efdd19636340318999174557";
			clientPblKey = new RsaKeyPair(null, "FZLHEUQxCEMr+jMkAz4S+y9pvWeCngTf9wECAH5frri6ZQemFqBHnaM+XRJ40grEL8WJKYoVaYy6doQHVW1H5QqEGLlDXjIjWlurt2eX45rAlNQJHkE/OM3p4hhKEG5oqdEHA5by+gx6h4Zk7OBjKjMnVSKRHdah+/pfPdzb3i5soxLBM9xqZ7qX575J2648HCF+WHPLn4Hsy8Um5GqxQARVnc9I+l6cowy3jYMEFnMWhXPOPnlWvgiYh/pFgZEP5qFVd1zCmXVmuUiFXfYCrKXVEXrQ40rGdMOUUJ+0019SRmlTuYAim5R15YLBRMzCc4L3IN2mY9QR3p1EU89CIyude1HGffTccKp13+NHQAu5BN7lMlmVrd5xFfwF8PDqyBQKyGre5v8H/AA=");

			Console.WriteLine();
			Console.WriteLine($"Client: {clientId} with public key: {clientPblKey.Public}");
			Console.WriteLine();
		}

		static async Task RegisterAppTestAsync(string clientId)
		{
			string httpcmd = "POST";
			string cmd = "registerapp";

			Uri uri = new Uri(ApiUri, $"{cmd}/{clientId}");

			string data = appKeys.Public;

			data = RsaCryptographerHelper.Encrypt(data, clientPblKey);

			data = Obfuscator.Obfuscate(data);

			var postData = new StringContent($"'{data}'", Encoding.UTF8, "application/json");

			string response = await ExecuteCommandAsync(httpcmd, uri, postData);

			appToken = response;

			Console.WriteLine();
			Console.WriteLine($"Registered app: {appToken}");
			Console.WriteLine();
		}

		static async Task OpenStorageTestAsync(string appToken, string storageId)
		{
			string httpcmd = "GET";
			string cmd = "open";

			Uri uri = new Uri(ApiUri, $"{cmd}/{appToken},{storageId}");

			string response = await ExecuteCommandAsync(httpcmd, uri);

			string[] storageData = response.Split(commaCharArray, 2);

			string signature = storageData[0];
			string data = storageData[1];

			bool ok = RsaCryptographerHelper.Verify(data, signature, clientPblKey);
			if (!ok)
			{
				Console.WriteLine("ERROR: data NOT verified.");
				return;
			}

			data = RsaCryptographerHelper.Decrypt(data, appKeys);

			string[] storageTokenAndPublicKey = data.Split(commaCharArray, 2);
			storageToken = storageTokenAndPublicKey[0];
			storagePblKey = new RsaKeyPair(null, storageTokenAndPublicKey[1]);

			Console.WriteLine();
			Console.WriteLine($"Opened storage: {storageToken} with public key: {storagePblKey.Public}");
			Console.WriteLine();
		}

		static async Task StoreThingTestAsync(string storageToken, string thingId, string data, DateTime modifiedOn)
		{
			string httpcmd = "PUT";
			string cmd = "store";

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{thingId}");

			long bModifiedOn = modifiedOn.ToUniversalTime().ToBinary();

			data = $"{bModifiedOn.ToString()},{data}";

			data = RsaCryptographerHelper.Encrypt(data, storagePblKey);

			string signature = RsaCryptographerHelper.Sign(data, appKeys);

			data = $"{signature},{data}";

			data = Obfuscator.Obfuscate(data);

			var postData = new StringContent($"'{data}'", Encoding.UTF8, "application/json");

			await ExecuteCommandAsync(httpcmd, uri, postData);

			Console.WriteLine();
		}

		static async Task ThingExistsTestAsync(string storageToken, string thingId)
		{
			string httpcmd = "GET";
			string cmd = "exists";

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{thingId}");

			string response = await ExecuteCommandAsync(httpcmd, uri);
			// response == YES or NO

			Console.WriteLine();
		}

		static async Task GetThingModifiedOnTestAsync(string storageToken, string thingId)
		{
			string httpcmd = "GET";
			string cmd = "getmodifiedon";

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{thingId}");

			string response = await ExecuteCommandAsync(httpcmd, uri);
			// response == DateTime as binary

			DateTime modifiedOn = DateTime.FromBinary(long.Parse(response));
			DateTime localModifiedOn = modifiedOn.ToLocalTime();

			Console.WriteLine();
			Console.WriteLine($"Thing modified on: UTC: {modifiedOn}, Local: {localModifiedOn}");
			Console.WriteLine();
		}

		static async Task GetThingTestAsync(string storageToken, string thingId)
		{
			string httpcmd = "GET";
			string cmd = "getacopy";

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{thingId}");

			string response = await ExecuteCommandAsync(httpcmd, uri);

			string[] signatureAndData = response.Split(commaCharArray, 2);
			string signature = signatureAndData[0];
			string thingData = signatureAndData[1];

			bool ok = RsaCryptographerHelper.Verify(thingData, signature, storagePblKey);
			if (!ok)
			{
				Console.WriteLine("ERROR: data NOT verified.");
				return;
			}

			thingData = RsaCryptographerHelper.Decrypt(thingData, appKeys);
			// modifiedOn,data

			Console.WriteLine();
			Console.WriteLine($"Thing all data: {thingData}");

			string[] modifiedOnAndData = thingData.Split(commaCharArray, 2);
			DateTime modifiedOn = DateTime.FromBinary(long.Parse(modifiedOnAndData[0]));
			DateTime localModifiedOn = modifiedOn.ToLocalTime();
			thingData = modifiedOnAndData[1];

			Console.WriteLine($"Thing data: {thingData}");
			Console.WriteLine($"Thing modified on: UTC: {modifiedOn}, Local: {localModifiedOn}");
			Console.WriteLine();
		}

		static async Task DiscardThingTestAsync(string storageToken, string thingId)
		{
			string httpcmd = "DELETE";
			string cmd = "discard";

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{thingId}");

			await ExecuteCommandAsync(httpcmd, uri);

			Console.WriteLine();
		}

		static async Task FindThingIdsTestAsync(string storageToken, string pattern)
		{
			string httpcmd = "GET";
			string cmd = "findids";

			if (string.IsNullOrWhiteSpace(pattern))
				pattern = " ";

			pattern = Uri.EscapeDataString(pattern);

			Uri uri = new Uri(ApiUri, $"{cmd}/{storageToken},{pattern}");

			string response = await ExecuteCommandAsync(httpcmd, uri);
			// response == null or encrypted/signed id list with separator |

			Console.WriteLine();

			if (response != null)
			{
				string[] signatureAndIds = response.Split(commaCharArray, 2);
				string signature = signatureAndIds[0];
				string ids = signatureAndIds[1];

				bool ok = RsaCryptographerHelper.Verify(ids, signature, storagePblKey);
				if (!ok)
				{
					Console.WriteLine("ERROR: data NOT verified.");
					return;
				}

				ids = RsaCryptographerHelper.Decrypt(ids, appKeys);

				Console.WriteLine($"Found: {ids}");
			}
			else
				Console.WriteLine($"Found: nothing");

			Console.WriteLine();
		}

		static async Task TestsAsync()
		{
			await NewClientTestAsync();

			await RegisterAppTestAsync(clientId);

			await OpenStorageTestAsync(appToken, "test");

			await OpenStorageTestAsync(appToken, "test2");

			await OpenStorageTestAsync(appToken, "test");

			await StoreThingTestAsync(storageToken, "test thing", "ala ma kota i psa ąęłóżść", (DateTime.Now - TimeSpan.FromHours(3)));
			//for (int i = 0; i < 100; i++)
			//{
			//	await StoreThingTestAsync(storageToken, "test thing " + i.ToString(), "ala ma kota i psa ąęłóżść", (DateTime.Now - TimeSpan.FromHours(3)));
			//}

			await ThingExistsTestAsync(storageToken, "test thing");

			await ThingExistsTestAsync(storageToken, "test thing W");

			await GetThingModifiedOnTestAsync(storageToken, "test thing");

			await GetThingTestAsync(storageToken, "test thing");

			//await DiscardThingTestAsync(storageToken, "test thing");

			await FindThingIdsTestAsync(storageToken, "");

			await FindThingIdsTestAsync(storageToken, "9$");

			await FindThingIdsTestAsync(storageToken, "a9$");
		}

		static async Task StressTestsAsync()
		{
			List<Task> l = new List<Task>();
			for (int i = 0; i < 100; i++)
			{
				l.Add(TestsAsync());
			}

			await Task.WhenAll(l);
		}

		static async Task StartTestsAsync()
		{
			try
			{
				await InitializeAsync();

				await TestsAsync();

				//await StressTestsAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
		}

		static void Main(string[] args)
		{
			//RsaCryptographer c = new RsaCryptographer();
			//IAsymmetricCryptographerKeyPair keys = c.GenerateKeyPair();

			//RsaKeyPair rkeysprv = new RsaKeyPair(keys.Private, null);
			//RsaKeyPair rkeyspbl = new RsaKeyPair(null, keys.Public);

			////ByteBuffer b = c.Encrypt(new ByteBuffer("<?xml version='1.0' encoding='utf-8'?><Project DefaultTargets='Build' ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'><PropertyGroup><Configuration Condition=' '$(Configuration)' == '' '>Debug</Configuration><Platform Condition=' '$(Platform)' == '' '>AnyCPU</Platform><ProjectGuid>{BA60D111-A7E1-4455-B192-450846976D49}</ProjectGuid><OutputType>Library</OutputType><RootNamespace>pbXNet</RootNamespace><AssemblyName>pbXNet</AssemblyName><TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion><ReleaseVersion>1.0.0.20</ReleaseVersion><SynchReleaseVersion>false</SynchReleaseVersion></PropertyGroup><PropertyGroup Condition=' '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' '><DebugSymbols>true</DebugSymbols><DebugType>full</DebugType><Optimize>false</Optimize><OutputPath>bin\\Debug</OutputPath><DefineConstants>DEBUG;NET461</DefineConstants><ErrorReport>prompt</ErrorReport><WarningLevel>4</WarningLevel><ConsolePause>false</ConsolePause></PropertyGroup><PropertyGroup Condition=' '$(Configuration)|$(Platform)' == 'Release|AnyCPU' '><Optimize>true</Optimize><OutputPath>bin\\Release</OutputPath><ErrorReport>prompt</ErrorReport><WarningLevel>4</WarningLevel><ConsolePause>false</ConsolePause><DefineConstants>NET461</DefineConstants></PropertyGroup><ItemGroup><Reference Include='Microsoft.Azure.KeyVault.Core, Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35, processorArchitecture=MSIL'><HintPath>..\\..\\Tools\\packages\\Microsoft.Azure.KeyVault.Core.2.0.4\\lib\\net45\\Microsoft.Azure.KeyVault.Core.dll</HintPath></Reference><Reference Include='System' /><Reference Include='System.ValueTuple, Version=4.0.1.1, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51, processorArchitecture=MSIL'><HintPath>..\\..\\WebApplication1\\packages\\System.ValueTuple.4.3.1\\lib\\netstandard1.0\\System.ValueTuple.dll</HintPath></Reference><Reference Include='Newtonsoft.Json'><HintPath>..\\packages\\Newtonsoft.Json.10.0.3\\lib\\net45\\Newtonsoft.Json.dll</HintPath></Reference><Reference Include='Microsoft.Data.Edm'><HintPath>..\\packages\\Microsoft.Data.Edm.5.8.2\\lib\\net40\\Microsoft.Data.Edm.dll</HintPath></Reference><Reference Include='System.Spatial'><HintPath>..\\packages\\System.Spatial.5.8.2\\lib\\net40\\System.Spatial.dll</HintPath></Reference><Reference Include='Microsoft.Data.OData'><HintPath>..\\packages\\Microsoft.Data.OData.5.8.2\\lib\\net40\\Microsoft.Data.OData.dll</HintPath></Reference><Reference Include='Microsoft.Data.Services.Client'><HintPath>..\\packages\\Microsoft.Data.Services.Client.5.8.2\\lib\\net40\\Microsoft.Data.Services.Client.dll</HintPath></Reference><Reference Include='Microsoft.WindowsAzure.Storage'><HintPath>..\\packages\\WindowsAzure.Storage.8.1.4\\lib\\net45\\Microsoft.WindowsAzure.Storage.dll</HintPath></Reference><Reference Include='System.Data' /></ItemGroup><ItemGroup><Compile Include='..\\pbXNet\\NETStd2\\AesCryptographer.cs'><Link>pbXNet\\NETStd2\\AesCryptographer.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\BinarySerializer.cs'><Link>pbXNet\\NETStd2\\BinarySerializer.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\DeviceFileSystem.cs'><Link>pbXNet\\NETStd2\\DeviceFileSystem.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\RsaCryptographer.cs'><Link>pbXNet\\NETStd2\\RsaCryptographer.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\Locale.cs'><Link>pbXNet\\Templates\\Locale.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\SecretsManager.cs'><Link>pbXNet\\Templates\\SecretsManager.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\Tools.cs'><Link>pbXNet\\Templates\\Tools.cs</Link></Compile><Compile Include='..\\pbXNet\\pbXNet.AssemblyInfo.cs'><Link>Properties\\pbXNet.AssemblyInfo.cs</Link></Compile></ItemGroup><ItemGroup><None Include='app.config' /><None Include='packages.config' /></ItemGroup><Import Project='..\\pbXNet\\pbXNet.Shared.projitems' Label='Shared' Condition='Exists('..\\pbXNet\\pbXNet.Shared.projitems')' /><Import Project='$(MSBuildBinPath)\\Microsoft.CSharp.targets' /></Project>0000000000000000000000000000000000000000000<?xml version='1.0' encoding='utf-8'?><Project DefaultTargets='Build' ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'><PropertyGroup><Configuration Condition=' '$(Configuration)' == '' '>Debug</Configuration><Platform Condition=' '$(Platform)' == '' '>AnyCPU</Platform><ProjectGuid>{BA60D111-A7E1-4455-B192-450846976D49}</ProjectGuid><OutputType>Library</OutputType><RootNamespace>pbXNet</RootNamespace><AssemblyName>pbXNet</AssemblyName><TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion><ReleaseVersion>1.0.0.20</ReleaseVersion><SynchReleaseVersion>false</SynchReleaseVersion></PropertyGroup><PropertyGroup Condition=' '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' '><DebugSymbols>true</DebugSymbols><DebugType>full</DebugType><Optimize>false</Optimize><OutputPath>bin\\Debug</OutputPath><DefineConstants>DEBUG;NET461</DefineConstants><ErrorReport>prompt</ErrorReport><WarningLevel>4</WarningLevel><ConsolePause>false</ConsolePause></PropertyGroup><PropertyGroup Condition=' '$(Configuration)|$(Platform)' == 'Release|AnyCPU' '><Optimize>true</Optimize><OutputPath>bin\\Release</OutputPath><ErrorReport>prompt</ErrorReport><WarningLevel>4</WarningLevel><ConsolePause>false</ConsolePause><DefineConstants>NET461</DefineConstants></PropertyGroup><ItemGroup><Reference Include='Microsoft.Azure.KeyVault.Core, Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35, processorArchitecture=MSIL'><HintPath>..\\..\\Tools\\packages\\Microsoft.Azure.KeyVault.Core.2.0.4\\lib\\net45\\Microsoft.Azure.KeyVault.Core.dll</HintPath></Reference><Reference Include='System' /><Reference Include='System.ValueTuple, Version=4.0.1.1, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51, processorArchitecture=MSIL'><HintPath>..\\..\\WebApplication1\\packages\\System.ValueTuple.4.3.1\\lib\\netstandard1.0\\System.ValueTuple.dll</HintPath></Reference><Reference Include='Newtonsoft.Json'><HintPath>..\\packages\\Newtonsoft.Json.10.0.3\\lib\\net45\\Newtonsoft.Json.dll</HintPath></Reference><Reference Include='Microsoft.Data.Edm'><HintPath>..\\packages\\Microsoft.Data.Edm.5.8.2\\lib\\net40\\Microsoft.Data.Edm.dll</HintPath></Reference><Reference Include='System.Spatial'><HintPath>..\\packages\\System.Spatial.5.8.2\\lib\\net40\\System.Spatial.dll</HintPath></Reference><Reference Include='Microsoft.Data.OData'><HintPath>..\\packages\\Microsoft.Data.OData.5.8.2\\lib\\net40\\Microsoft.Data.OData.dll</HintPath></Reference><Reference Include='Microsoft.Data.Services.Client'><HintPath>..\\packages\\Microsoft.Data.Services.Client.5.8.2\\lib\\net40\\Microsoft.Data.Services.Client.dll</HintPath></Reference><Reference Include='Microsoft.WindowsAzure.Storage'><HintPath>..\\packages\\WindowsAzure.Storage.8.1.4\\lib\\net45\\Microsoft.WindowsAzure.Storage.dll</HintPath></Reference><Reference Include='System.Data' /></ItemGroup><ItemGroup><Compile Include='..\\pbXNet\\NETStd2\\AesCryptographer.cs'><Link>pbXNet\\NETStd2\\AesCryptographer.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\BinarySerializer.cs'><Link>pbXNet\\NETStd2\\BinarySerializer.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\DeviceFileSystem.cs'><Link>pbXNet\\NETStd2\\DeviceFileSystem.cs</Link></Compile><Compile Include='..\\pbXNet\\NETStd2\\RsaCryptographer.cs'><Link>pbXNet\\NETStd2\\RsaCryptographer.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\Locale.cs'><Link>pbXNet\\Templates\\Locale.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\SecretsManager.cs'><Link>pbXNet\\Templates\\SecretsManager.cs</Link></Compile><Compile Include='..\\pbXNet\\Templates\\Tools.cs'><Link>pbXNet\\Templates\\Tools.cs</Link></Compile><Compile Include='..\\pbXNet\\pbXNet.AssemblyInfo.cs'><Link>Properties\\pbXNet.AssemblyInfo.cs</Link></Compile></ItemGroup><ItemGroup><None Include='app.config' /><None Include='packages.config' /></ItemGroup><Import Project='..\\pbXNet\\pbXNet.Shared.projitems' Label='Shared' Condition='Exists('..\\pbXNet\\pbXNet.Shared.projitems')' /><Import Project='$(MSBuildBinPath)\\Microsoft.CSharp.targets' /></Project>", Encoding.UTF8), rkeyspbl);
			//ByteBuffer b = c.Encrypt(new ByteBuffer("01234567890123456789012345678901234567890123456789012345678912345!", Encoding.UTF8), rkeyspbl);
			//Console.WriteLine(b.ToHexString());

			//ByteBuffer s = c.Sign(b, rkeysprv);
			//Console.WriteLine(s.ToHexString());

			//bool ok = c.Verify(b, s, rkeyspbl);
			//Console.WriteLine($"{ok}");

			//string d = c.Decrypt(b, rkeysprv).ToString(Encoding.UTF8);
			//Console.WriteLine(d);


			StartTestsAsync();

			Console.ReadKey();
		}
	}
}