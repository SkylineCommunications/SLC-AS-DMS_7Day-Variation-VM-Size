namespace SLProcess_VM_Size_7day_Variation_1
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using Newtonsoft.Json;

	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Common;
	using Skyline.DataMiner.Net.Messages;
	using Skyline.DataMiner.Net.Trending;


	using Consts = Library.Consts.MicrosoftPlatformConsts;
	using ElementState = Skyline.DataMiner.Core.DataMinerSystem.Common.ElementState;


	/// <summary>
	/// Represents a DataMiner Automation script.
	/// </summary>
	public class Script
	{
		public static string FormatSize(double? sizeInKb)
		{
			if (!sizeInKb.HasValue)
			{
				return "N/A";
			}

			double absoluteSizeInKb = Math.Abs(sizeInKb.Value);
			string sign = sizeInKb < 0 ? "-" : String.Empty;

			return $"{sign}{Math.Round(absoluteSizeInKb, 1)}";
		}

		public static double? GetAverageDifference(double olderAverage, double newerAverage)
		{
			double? difference = null;

			if (olderAverage > 0 && newerAverage > 0)
			{
				difference = newerAverage - olderAverage;
			}

			return difference;
		}

		public static Dictionary<string, double> GetAverageVmSizes(IDmsElement element, IEnumerable<string> processNames, DateTime startTime, DateTime endTime, IEngine engine)
		{
			var averages = new Dictionary<string, double>();
			int taskManagerVmSizePid = 99;
			var parameters = processNames.Select(processName => new ParameterIndexPair(taskManagerVmSizePid, processName)).ToArray();
			var message = new GetTrendDataMessage
			{
				DataMinerID = element.AgentId,
				ElementID = element.Id,
				Parameters = parameters,
				StartTime = startTime,
				EndTime = endTime,
				TrendingType = TrendingType.Average,
				ReturnAsObjects = true,
			};

			DMSMessage[] response = engine.SendSLNetMessage(message);

			GetTrendDataResponseMessage trendDataResponseMessage = (GetTrendDataResponseMessage)response[0];

			if (trendDataResponseMessage?.Records != null)
			{
				ProcessRecords(averages, trendDataResponseMessage, engine);
			}

			return averages;
		}

		public static void ProcessRecords(Dictionary<string, double> averages, GetTrendDataResponseMessage trendDataResponseMessage, IEngine engine)
		{
			var records = trendDataResponseMessage.Records;

			if (records == null)
			{
				return;
			}

			foreach (string key in records.Keys)
			{
				var trendRecords = records[key];
				var splitKey = key.Split('/');
				var processName = splitKey.Length > 1 ? splitKey[1].ToUpperInvariant() : null;

				if (trendRecords == null)
				{
					continue;
				}

				foreach (TrendRecord trendRecord in trendRecords)
				{
					AverageTrendRecord averageTrendRecord = trendRecord as AverageTrendRecord;

					if (averageTrendRecord == null)
					{
						continue;
					}

					double average = averageTrendRecord.AverageValue;

					if (!averages.ContainsKey(processName))
					{
						averages[processName] = average;
					}
				}
			}
		}

		/// <summary>
		/// The script entry point.
		/// </summary>
		/// <param name="engine">Link with SLAutomation process.</param>
		public void Run(IEngine engine)
		{
			try
			{
				RunSafe(engine);
			}
			catch (ScriptAbortException)
			{
				// Catch normal abort exceptions (engine.ExitFail or engine.ExitSuccess)
				throw; // Comment if it should be treated as a normal exit of the script.
			}
			catch (ScriptForceAbortException)
			{
				// Catch forced abort exceptions, caused via external maintenance messages.
				throw;
			}
			catch (ScriptTimeoutException)
			{
				// Catch timeout exceptions for when a script has been running for too long.
				throw;
			}
			catch (InteractiveUserDetachedException)
			{
				// Catch a user detaching from the interactive script by closing the window.
				// Only applicable for interactive scripts, can be removed for non-interactive scripts.
				throw;
			}
			catch (Exception e)
			{
				engine.ExitFail("Run|Something went wrong: " + e);
			}
		}

		private void RunSafe(IEngine engine)
		{
			List<TestResult> results = new List<TestResult>();

			IDms dms = engine.GetDms();
			var elements = dms.GetElements().Where(e => e.Protocol.Name.Equals("Microsoft Platform") && e.State == ElementState.Active);

			foreach (var element in elements)
			{
				var computerName = element.GetStandaloneParameter<string>(Consts.ComputerNameParam).GetValue();
				var now = DateTime.Now;
				var processes = element.GetTable(Consts.TaskManagerTable).GetRows();
				var processNames = processes.Select(x => Convert.ToString(x[0])).ToList();
				var averageVmSizes24Hours = GetAverageVmSizes(element, processNames, now.AddHours(-24), now, engine);

				var averageVmSizes7Days = GetAverageVmSizes(element, processNames, now.AddDays(-7), now.AddDays(-6), engine);

				foreach (var process in processes)
				{
					var processName = Convert.ToString(process[0]);
					double? trendDifference24Hours = null;
					var formattedkey = processName.ToUpperInvariant();
					double temp;
					if (averageVmSizes7Days.TryGetValue(formattedkey, out temp) && averageVmSizes24Hours.TryGetValue(formattedkey, out temp))
					{
						trendDifference24Hours = GetAverageDifference(averageVmSizes7Days[formattedkey], averageVmSizes24Hours[formattedkey]);

						TestResult testResult = new TestResult
						{
							ParameterName = "7 Day Variation VmSize",
							DmaName = computerName,
							ElementName = element.Name,
							ReceivedValue = FormatSize(trendDifference24Hours),
							DisplayName = processName,
						};

						results.Add(testResult);
					}
				}
			}

			if (results.Count > 0)
			{
				engine.AddScriptOutput("result", JsonConvert.SerializeObject(results));
			}
		}


		public class TestResult
		{
			public string ParameterName { get; set; }

			public string DisplayName { get; set; }

			public string ElementName { get; set; }

			public string DmaName { get; set; }

			public string ReceivedValue { get; set; }

			public string ExpectedValue { get; set; }

			public bool Success { get; set; }
		}
	}
}

//---------------------------------
// Consts\MicrosoftPlatformConsts.cs
//---------------------------------
namespace Library.Consts
{
	using System.Collections.Generic;

	public static class MicrosoftPlatformConsts
	{
		public static readonly int ComputerNameParam = 165;
		public static readonly int LastRebootDaysParam = 209;
		public static readonly int LastUpdateDaysParam = 208;
		public static readonly int TotalProcessorParam = 350;
		public static readonly int PhysicalMemoryUsageParam = 51321;
		public static readonly int DriveTableId = 170;
		public static readonly int DriveFreeSpaceColumn = 3;
		public static readonly int TaskManagerTable = 96;
		public static readonly int TaskManagerVmSizePid = 99;
		public static readonly Dictionary<int, (int TaskManagerCpuIdx, int TaskManagerVMSizeIdx)> TaskManagerIdxsbyBranch = new Dictionary<int, (int, int)>
		{
			[6] = (11, 4),
			[1] = (15, 12),
		};
	}
}
