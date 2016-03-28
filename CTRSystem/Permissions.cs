﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CTRSystem
{
	public static class Permissions
	{
		public static readonly string Auth = "ctrs.auth";
		public static readonly string Admin = "ctrs.admin";
		public static readonly string Commands = "ctrs.commands";

		public const string RestTransaction = "ctrs.rest.transaction";
	}
}
