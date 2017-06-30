﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using pbXNet;

namespace pbXStorage.Server.NETCore
{
	class SimpleCryptographer2DataProtector : ISimpleCryptographer
	{
		IDataProtector _protector;

		public SimpleCryptographer2DataProtector(IDataProtector protector)
		{
			_protector = protector;
		}

		public string Encrypt(string data) => _protector.Protect(data);
		public string Decrypt(string data) => _protector.Unprotect(data);
	}
}
