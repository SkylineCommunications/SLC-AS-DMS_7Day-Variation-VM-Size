/*
****************************************************************************
*  Copyright (c) 2024,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

31/07/2024	1.0.0.1		SNA, Skyline	Initial version
****************************************************************************
*/

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
			int taskManagerTable = 96;
			int computerNameParam = 165;
			IDms dms = engine.GetDms();
			var elements = dms.GetElements().Where(e => e.Protocol.Name.Equals("Microsoft Platform") && e.State == ElementState.Active);

			foreach (var element in elements)
			{
				var computerName = element.GetStandaloneParameter<string>(computerNameParam).GetValue();
				var now = DateTime.Now;
				var processes = element.GetTable(taskManagerTable).GetRows();
				var processNames = processes.Select(x => Convert.ToString(x[0])).ToList();
				var averageVmSizes24Hours = GetAverageVmSizes(element, processNames, now.AddHours(-24), now, engine);

				var averageVmSizes7Days = GetAverageVmSizes(element, processNames, now.AddDays(-7), now.AddDays(-6), engine);

				foreach (var process in processes)
				{
					var processName = Convert.ToString(process[0]);
					double? trendDifference24Hours = null;
					var formattedkey = processName.ToUpperInvariant();
					if (averageVmSizes7Days.TryGetValue(formattedkey, out _) && averageVmSizes24Hours.TryGetValue(formattedkey, out _))
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
