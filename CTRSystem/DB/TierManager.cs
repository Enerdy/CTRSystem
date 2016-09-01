﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace CTRSystem.DB
{
	public class TierManager
	{
		private IDbConnection db;
		private List<Tier> _cache = new List<Tier>();
		public List<Tier> Cache
		{
			get { return _cache; }
		}

		public TierManager(IDbConnection db)
		{
			this.db = db;

			var creator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite
				? (IQueryBuilder)new SqliteQueryCreator()
				: new MysqlQueryCreator());

			if (creator.EnsureTableStructure(new SqlTable(CTRS.Config.TierTableName,
					new SqlColumn("ID", MySqlDbType.Int32) { AutoIncrement = true, Primary = true },
					new SqlColumn("Name", MySqlDbType.VarChar) { Length = 12, Unique = true },
					new SqlColumn("CreditsRequired", MySqlDbType.Float) { NotNull = true, DefaultValue = "0" },
					new SqlColumn("ShortName", MySqlDbType.VarChar) { NotNull = true, DefaultValue = "", Length = 6 },
					new SqlColumn("ChatColor", MySqlDbType.Text) { NotNull = true, DefaultValue = "" },
					new SqlColumn("Permissions", MySqlDbType.Text) { NotNull = true, DefaultValue = "" },
					new SqlColumn("ExperienceMultiplier", MySqlDbType.Float) { NotNull = true, DefaultValue = "1" })))
			{
				TShock.Log.ConsoleInfo($"CTRS: created table '{CTRS.Config.TierTableName}'");
			}

			// Load all tiers to the cache
			Task.Run(async () => _cache = await GetAllAsync());
		}

		public Tier Get(int id)
		{
			return _cache.Find(t => t.ID == id);
		}

		public Task<Tier> GetAsync(int id)
		{
			return Task.Run(() =>
			{
				Tier tier = _cache.Find(t => t.ID == id);
				if (tier != null)
					return tier;
				else
				{
					string query = $"SELECT * FROM {CTRS.Config.TierTableName} WHERE ID = @0;";
					using (var result = db.QueryReader(query, id))
					{
						if (result.Read())
						{
							tier = new Tier(id)
							{
								Name = result.Get<string>("Name"),
								CreditsRequired = result.Get<float>("CreditsRequired"),
								ShortName = result.Get<string>("ShortName"),
								ChatColor = Tools.ColorFromRGB(result.Get<string>("ChatColor")),
								Permissions = result.Get<string>("Permissions").Split(',').ToList(),
								ExperienceMultiplier = result.Get<float>("ExperienceMultiplier")
							};
							_cache.Add(tier);
							return tier;
						}
						else
							throw new TierNotFoundException(id);
					}
				}
			});
		}

		public Task<Tier> GetAsync(string name)
		{
			return Task.Run(() =>
			{
				Tier tier = _cache.Find(t => t.Name == name);
				if (tier != null)
					return tier;
				else
				{
					string query = $"SELECT * FROM {CTRS.Config.TierTableName} WHERE Name = @0;";
					using (var result = db.QueryReader(query, name))
					{
						if (result.Read())
						{
							tier = new Tier(result.Get<int>("ID"))
							{
								Name = result.Get<string>("Name"),
								CreditsRequired = result.Get<float>("CreditsRequired"),
								ShortName = result.Get<string>("ShortName"),
								ChatColor = Tools.ColorFromRGB(result.Get<string>("ChatColor")),
								Permissions = result.Get<string>("Permissions").Split(',').ToList(),
								ExperienceMultiplier = result.Get<float>("ExperienceMultiplier")
							};
							_cache.Add(tier);
							return tier;
						}
						else
							throw new TierNotFoundException(name);
					}
				}
			});
		}

		public Tier GetByCredits(float totalcredits)
		{
			return _cache.FindAll(t => t.CreditsRequired <= totalcredits).OrderBy(t => t.CreditsRequired).LastOrDefault();
		}

		public Task<Tier> GetByCreditsAsync(float totalcredits)
		{
			return Task.Run(() =>
			{
				string query = $"SELECT * FROM {CTRS.Config.TierTableName} WHERE CreditsRequired <= @0 ORDER BY CreditsRequired DESC LIMIT 1;";
				using (var result = db.QueryReader(query, totalcredits))
				{
					if (result.Read())
					{
						Tier tier = new Tier(result.Get<int>("ID"))
						{
							Name = result.Get<string>("Name"),
							CreditsRequired = result.Get<float>("CreditsRequired"),
							ShortName = result.Get<string>("ShortName"),
							ChatColor = Tools.ColorFromRGB(result.Get<string>("ChatColor")),
							Permissions = result.Get<string>("Permissions").Split(',').ToList(),
							ExperienceMultiplier = result.Get<float>("ExperienceMultiplier")
						};
						if (!_cache.Contains(tier))
							_cache.Add(tier);
						return tier;
					}
					throw new TierNotFoundException(null);
				}
			});
		}

		public Task<List<Tier>> GetAllAsync()
		{
			return Task.Run(() =>
			{
				List<Tier> list = new List<Tier>();
				string query = $"SELECT * FROM {CTRS.Config.TierTableName};";
				using (var result = db.QueryReader(query))
				{
					while (result.Read())
					{
						list.Add(new Tier(result.Get<int>("ID"))
						{
							Name = result.Get<string>("Name"),
							CreditsRequired = result.Get<int>("CreditsRequired"),
							ShortName = result.Get<string>("ShortName"),
							ChatColor = Tools.ColorFromRGB(result.Get<string>("ChatColor")),
							Permissions = result.Get<string>("Permissions").Split(',').ToList(),
							ExperienceMultiplier = result.Get<float>("ExperienceMultiplier")
						});
					}
				}
				return list;
			});
		}

		public async Task UpgradeTier(Contributor contributor, bool suppressNotifications = false)
		{
			if (suppressNotifications
				|| (contributor.Notifications & Notifications.TierUpdate) == Notifications.TierUpdate)
			{
				ContributorUpdates updates = 0;
				Tier tier = await GetByCreditsAsync(contributor.TotalCredits);
				if (contributor.Tier != tier.ID)
				{
					contributor.Tier = tier.ID;

					// Don't touch notifications on suppress
					if (!suppressNotifications)
					{
						contributor.Notifications |= Notifications.NewTier;
						contributor.Notifications ^= Notifications.TierUpdate;
						updates |= ContributorUpdates.Notifications;
					}

					updates |= ContributorUpdates.Tier;
				}


				if (!await CTRS.Contributors.UpdateAsync(contributor, updates))
					TShock.Log.ConsoleError("CTRS-DB: something went wrong while updating a contributor's notifications.");
			}
		}

		/// <summary>
		/// Reloads all tiers currently in cache, removing outdated ones.
		/// </summary>
		public async void Refresh()
		{
			if (_cache.Count == 0)
				return;

			List<Tier> tiers = await GetAllAsync();
			lock (_cache)
			{
				for (int i = 0; i < _cache.Count; i++)
				{
					if (!tiers.Contains(_cache[i]))
						_cache.RemoveAt(i);
					else
						_cache[i] = tiers.Find(t => t.ID == _cache[i].ID);
				}
			}
		}

		public class TierManagerException : Exception
		{
			public TierManagerException(string message) : base(message)
			{

			}

			public TierManagerException(string message, Exception inner) : base(message, inner)
			{

			}
		}

		public class TierNotFoundException : TierManagerException
		{
			public TierNotFoundException(int id) : base($"Tier ID:{id} does not exist")
			{

			}

			public TierNotFoundException(string name) : base($"Tier '{name}' does not exist")
			{

			}
		}
	}
}
