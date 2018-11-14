using System;
using NAppUpdate.Framework;
using NAppUpdate.Framework.Sources;
using NAppUpdate.Framework.FeedReaders;
using System.IO;
using System.Text;
using System.Net;

class Options
{
	public string FeedUri { get; set; }
	public bool UpdateApplication { get; set; }
	public bool EnableLogging { get; set; }
	public bool Rollback { get; set; }
	public ICredentials Credentials { get; set; }

	public static string Usage()
	{
		StringBuilder b = new StringBuilder();
		b.AppendFormat("Usage:\n");
		b.AppendFormat("{0} <-f/--feed> uri [-u/--update] [-l/--logging] [-n/--no-rollback]\n",
			Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location));
		b.Append("-f/--feed uri\t\tGet feed file from URI and check updates\n");
		b.Append("-u/--update\t\tStart update if available (false by default)\n");
		b.Append("-l/--logging\t\tCreate update log in base directory (false by default)\n");
		b.Append("-n/--no-rollback\t\tDo not rollback if update error occured (true by default)\n");
		b.Append("-c/--credentials\t\tUse credentials for access to resource (example: \"anon:pass\")\n");
		return b.ToString();
	}
}

namespace NAppUpdate.Updater.Standalone
{
	class Program
	{
		public static void CancelHandler(object sender, ConsoleCancelEventArgs args)
		{
			UpdateManager.Instance.Abort(true);
			Environment.Exit(2);
		}

		static Options ParseArgsSimple(string[] args)
		{
			var opts = new Options
			{
				FeedUri = null,
				UpdateApplication = false,
				EnableLogging = false,
				Rollback = true,
			};

			for (int i = 0; i < args.Length; ++i)
			{
				string opt = args[i];

				switch (opt)
				{
					case "-f":
					case "--feed":
						if (i + 1 >= args.Length)
							throw new ArgumentException("Wrong arguments count");
						opts.FeedUri = args[i + 1];
						++i;
						continue;
					case "-u":
					case "--update":
						opts.UpdateApplication = true;
						continue;
					case "-l":
					case "--logging":
						opts.EnableLogging = true;
						continue;
					case "-n":
					case "--no-rollback":
						opts.Rollback = false;
						continue;
					case "-c":
					case "--credentials":
						if (i + 1 >= args.Length)
							throw new ArgumentException("Wrong arguments count");
						var creds = args[i + 1].Split(':');
						if (creds.Length != 2)
							throw new ArgumentException("Wrong credentials format");
						opts.Credentials = new NetworkCredential(creds[0], creds[1]);
						++i;
						continue;
					default:
						throw new ArgumentException("Unknown argument: " + opt);
				}
			}

			if (string.IsNullOrEmpty(opts.FeedUri))
				throw new ArgumentException("Not supplied feed URI");

			return opts;
		}

		static void Main(string[] args)
		{
			// exit codes
			// 0 - Update successful or no updates
			// 1 - Update available
			// 2 - Received stop signal
			// 3 - Error

			Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelHandler);

			Options opts = null;
			try
			{
				opts = ParseArgsSimple(args);
			}
			catch (ArgumentException ex)
			{
				Console.WriteLine(ex.ToString());
				Console.WriteLine(Options.Usage());
				Environment.Exit(3);
			}

			UpdateManager upd = UpdateManager.Instance;
			upd.Config.TempFolder = Path.GetTempPath();
			upd.UpdateFeedReader = new NauXmlFeedReader();
			upd.UpdateSource = new ResumableUriSource(opts.FeedUri);
			upd.UpdateCredentials = opts.Credentials;
			upd.MaximumRetries = 10;

			try
			{
				upd.CheckForUpdates();
			}
			catch (Exception ex)
			{
				if (ex is NAppUpdateException)
					Console.WriteLine("Updater exception");
				else
					Console.WriteLine("System exception");

				Console.WriteLine(ex.ToString());
				Environment.Exit(3);
			}

			if (upd.UpdatesAvailable == 0)
			{
				Console.WriteLine("Updates available: {0}", upd.UpdatesAvailable);
				Environment.Exit(0);
			}

			if (upd.UpdatesAvailable > 0 && !opts.UpdateApplication)
			{
				Console.WriteLine("Updates available: {0}", upd.UpdatesAvailable);
				Environment.Exit(1);
			}

			try
			{
				if (upd.UpdatesAvailable > 0)
					upd.PrepareUpdates();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Preparing failed: {0}", ex.ToString());
				Environment.Exit(3);
			}

			try
			{
				Console.WriteLine("Updates applied");
				upd.ApplyUpdates(false, opts.EnableLogging, false);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Applying updates failed: {0}", ex.ToString());
				if (opts.Rollback)
					upd.RollbackUpdates();
				Environment.Exit(3);
			}

			Environment.Exit(0);
		}
	}
}
