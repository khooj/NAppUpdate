using System;

namespace feedbuildercon
{
	class Program
	{
		static void Main(string[] args)
		{
			try
			{
				var m = new Main(args);
			}
			catch (SystemException ex)
			{
				Console.WriteLine("Error: " + ex.ToString());
				Environment.Exit(-1);
			}

			Environment.Exit(0);
		}
	}
}
