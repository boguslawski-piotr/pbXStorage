﻿using System.ComponentModel.DataAnnotations.Schema;

namespace pbXStorage.Server.AspNetCore.Data
{
	[Table("AspNetUserRepositories")]
	public class ApplicationUserRepository
	{
		public string RepositoryId { get; set; }
		public string ApplicationUserId { get; set; }
	}
}
