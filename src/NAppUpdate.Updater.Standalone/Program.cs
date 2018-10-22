using System;
using NAppUpdate.Framework;
using NAppUpdate.Framework.Sources;
using NAppUpdate.Framework.FeedReaders;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

class Options
{
	[Option('f', "feed", Required = true, HelpText = "XML Feed uri source")]
	public string FeedUri { get; set; }

	[Option('u', "update", Default = false, HelpText = "Update application")]
	public bool UpdateApplication { get; set; }

	[Usage(ApplicationAlias = "updater")]
	public static IEnumerable<Example> Examples
	{
		get
		{
			return new List<Example>()
			{
				new Example("Update application using FTP feed", new Options { FeedUri = "ftp://example.com/feed.xml", UpdateApplication = false })
			};
		}
	}
}

namespace NAppUpdate.Updater.Standalone
{
	class Program
	{
		public static void CancelHandler(object sender, ConsoleCancelEventArgs args)
		{
			Environment.Exit(2);
		}

		static void Main(string[] args)
		{
			// exit codes
			// 0 - Update successful or no updates
			// 1 - Update available
			// 2 - Received stop signal
			// 3 - Error

			Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelHandler);

			var opts = new Options();
			var args_result = Parser.Default.ParseArguments<Options>(args)
				.WithParsed(o => opts = o);

			UpdateManager upd = UpdateManager.Instance;
			upd.Config.TempFolder = Path.GetTempPath();
			upd.UpdateFeedReader = new NauXmlFeedReader();
			upd.UpdateSource = new ResumableUriSource(opts.FeedUri);
			upd.MaximumRetries = 100;

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

			Console.WriteLine("Updates available: {0}", upd.UpdatesAvailable);

			if (upd.UpdatesAvailable == 0)
				Environment.Exit(0);

			if (upd.UpdatesAvailable > 0 && !opts.UpdateApplication)
				Environment.Exit(1);

			try
			{
				Stopwatch sw = new Stopwatch();
				sw.Start();
				if (upd.UpdatesAvailable > 0)
					upd.PrepareUpdates();
				Console.WriteLine("Time preparing updates: {0}", sw.Elapsed.TotalSeconds);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Preparing failed: {0}", ex.ToString());
				Environment.Exit(3);
			}

			Console.WriteLine("Installing updates");

			try
			{
				UpdateManager.Instance.ApplyUpdates(false, true, true);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Applying updates failed: {0}", ex.ToString());
				Environment.Exit(3);
			}

			Console.WriteLine("Ended");
			Environment.Exit(0);
		}
	}
}
