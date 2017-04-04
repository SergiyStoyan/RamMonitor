//********************************************************************************************
//Author: Sergey Stoyan, CliverSoft.com
//        http://cliversoft.com
//        stoyan@cliversoft.com
//        sergey.stoyan@gmail.com
//        27 February 2007
//Copyright: (C) 2007, Sergey Stoyan
//********************************************************************************************

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Diagnostics;
using Cliver;
using System.Configuration;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net.Http;

namespace Cliver.RamMonitor
{
    public class Service
    {
        static Service()
        {
            Win32.SYSTEM_INFO si;
            Win32.GetSystemInfo(out si);
            process_min_address = (ulong)si.minimumApplicationAddress;
            process_max_address = (ulong)si.maximumApplicationAddress;

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            ServicePointManager.CheckCertificateRevocationLis‌​t = false;
        }
        static readonly ulong process_min_address;
        static readonly ulong process_max_address;
        static readonly uint MEMORY_BASIC_INFORMATION_size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win64.MEMORY_BASIC_INFORMATION));

        public delegate void OnStateChanged();
        public static event OnStateChanged StateChanged = null;

        public static bool Running
        {
            set
            {
                if (value)
                {
                    if (monitor_t == null || !monitor_t.IsAlive)
                    {
                        monitor_t = Cliver.ThreadRoutines.StartTry(
                            monitor,
                            null,
                            () =>
                            {
                                monitor_t = null;
                                StateChanged?.Invoke();
                            }
                            );
                    }
                }
                else
                {
                    if (monitor_t != null && monitor_t.IsAlive)
                    {
                        Thread exiting_monitor_t = monitor_t;
                        monitor_t = null;
                        //exiting_monitor_t.Join();
                        while (exiting_monitor_t.IsAlive)
                            Application.DoEvents();
                    }
                }
            }
            get
            {
                return monitor_t != null && monitor_t.IsAlive;
            }
        }
        static Thread monitor_t = null;

        static string ProcessName;
        static Regex DumpRegex;
        static string EventUrl;
        static uint CheckPeriodInSecs;
        static readonly Encoding Encoding = System.Text.Encoding.Unicode;

        static void monitor()
        {
            StateChanged?.Invoke();

            ProcessName = Settings.General.ProcessName;
            if (ProcessName == null)
                throw new Exception("ProcessName is not specified.");
            DumpRegex = Settings.General.DumpRegex;
            if (DumpRegex == null)
                throw new Exception("Regex is not specified.");
            EventUrl = Settings.General.EventUrl;
            if (string.IsNullOrWhiteSpace(EventUrl))
                throw new Exception("EventUrl is not specified.");
            CheckPeriodInSecs = Settings.General.CheckPeriodInSecs;
            if (CheckPeriodInSecs < 60)
                throw new Exception("CheckPeriodInSecs is < 60");
            //Encoding = System.Text.Encoding.GetEncoding(Settings.General.EncodingCodePage);
            //if (Encoding == null)
            //    throw new Exception("Encoding is not specified.");

            while (monitor_t != null)
            {
                DateTime next_check_time = DateTime.Now.AddSeconds(CheckPeriodInSecs);
                process(ProcessName);
                while (monitor_t != null && next_check_time > DateTime.Now)
                    Thread.Sleep(100);
            }
        }

        static void process(string process_name_pattern)
        {
            //Process[] ps = Process.GetProcessesByName(process_name);
            Regex process_name_regex = new Regex("^" + Regex.Replace(process_name_pattern, @"\*", ".*") + "$", RegexOptions.IgnoreCase);
            Process[] ps = Process.GetProcesses().Where(p => process_name_regex.IsMatch(p.ProcessName)).ToArray();

            if (ps.Length < 1)
                Log.Main.Warning("No process matching '" + process_name_pattern + "' exists.");
            else
            {
                foreach (Process p in ps)
                {
                    Log.Main.Inform("Dumping process: " + p.ProcessName + ", id: " + p.Id);
                    IntPtr ph = Win32.OpenProcess(Win32.ProcessRights.PROCESS_QUERY_INFORMATION | Win32.ProcessRights.PROCESS_WM_READ, false, p.Id);
                    if (ph == null || ph == IntPtr.Zero)
                    {
                        try
                        {
                            Process.EnterDebugMode();
                        }
                        catch (Exception e)
                        {
                            if (e is System.Security.SecurityException
                                || e is System.UnauthorizedAccessException
                                || e is System.ComponentModel.Win32Exception
                                )
                            {
                                if (ProcessRoutines.IsElevated())
                                    LogMessage.Exit("Despite the app is running with elevated privileges, it cannot EnterDebugMode. Please fix the problem before using the app.");
                                //string message = ProgramRoutines.GetAppName() + " needs administatrator privileges to monitor process '" + p.ProcessName + "'. It will restart now and ask for elevated privileges.";
                                //Log.Main.Inform(message);
                                //Cliver.Message.ShowDialog(Application.ProductName, System.Drawing.SystemIcons.Information, message, new string[1] { "OK" }, 0, null, null, null, true);
                                LogMessage.Inform(ProgramRoutines.GetAppName() + " needs administatrator privileges to monitor process '" + p.ProcessName + "'. It will restart now and ask for elevated privileges.");
                                ControlRoutines.InvokeFromUiThread((Action)delegate { ProcessRoutines.Restart(true); });
                            }
                            else
                                throw e;
                                //throw new Exception("OpenProcess failed:" + Win32Routines.GetLastErrorString());
                        }
                    }

                    List<List<string>> matches = new List<List<string>>();
                    string text0 = "";
                    Win64.MEMORY_BASIC_INFORMATION mbi;
                    //System.IO.StreamWriter sw = new System.IO.StreamWriter(@"ram.txt");
                    for (ulong address = process_min_address; address < process_max_address; address = mbi.BaseAddress + mbi.RegionSize)
                    {
                        if (monitor_t == null)
                            return;

                        if (1 > Win64.VirtualQueryEx(ph, new UIntPtr(address), out mbi, MEMORY_BASIC_INFORMATION_size))
                        {
                            //throw new Exception("VirtualQueryEx failed:" + Win32Routines.GetLastErrorMessage());
                            Log.Main.Error2("VirtualQueryEx failed:" + Win32Routines.GetLastErrorMessage());
                            break;
                        }
                        if ((mbi.State & Win32.MemoryState.MEM_COMMIT) != Win32.MemoryState.MEM_COMMIT)
                            continue;
                        if ((mbi.Protect & Win32.MemoryProtection.PAGE_NOACCESS) == Win32.MemoryProtection.PAGE_NOACCESS)
                            continue;
                        if ((mbi.Protect & Win32.MemoryProtection.PAGE_GUARD) == Win32.MemoryProtection.PAGE_GUARD)
                            continue;
                        byte[] bs = new byte[mbi.RegionSize];
                        int bytes_count = 0;
                        if (!Win64.ReadProcessMemory((int)ph, mbi.BaseAddress, bs, mbi.RegionSize, ref bytes_count))
                            Log.Main.Error2("ReadProcessMemory failed:" + Win32Routines.GetLastErrorMessage());
                        string text1 = Encoding.GetString(bs, 0, bytes_count);
                        int last_match_end;
                        parse(text0 + text1, ref matches, out last_match_end);
                        //sw.Write(text1 + "\r\n\r\n####################################\r\n\r\n");
                        int text0_length = BUFFER_PASS_SIZE;
                        if (last_match_end > 0)
                        {
                            text0_length = text1.Length - last_match_end;
                            if (text0_length > BUFFER_PASS_SIZE)
                                text0_length = BUFFER_PASS_SIZE;
                            else if (text0_length < 0)
                                text0_length = 0;
                        }
                        if (text1.Length > text0_length)
                            text0 = text1.Substring(text1.Length - text0_length);
                        else
                            text0 = "";
                    }
                    //sw.Close();
                    //if (matches.Count > 0)
                    {
                        Log.Main.Write("MATCHES:\r\n" + SerializationRoutines.Json.Serialize(matches));
                        post(new { Process = p.ProcessName, Regex = DumpRegex/*, Encoding = new { Name = Encoding.EncodingName, CodePage = Encoding.CodePage }*/, Matches = matches });
                    }
                }
            }
        }

        const int BUFFER_PASS_SIZE = 1000;

        static void parse(string text, ref List<List<string>> matches, out int last_match_end)
        {
            last_match_end = -1;
            for (Match m = DumpRegex.Match(text); m.Success; m = m.NextMatch())
            {
                last_match_end = m.Index + m.Length;
                List<string> gs = new List<string>();
                foreach (Group g in m.Groups)
                    gs.Add(g.Value);
                if (gs.Count > 0)
                    matches.Add(gs);
            }
        }

        static async void post(dynamic data)
        {
            try
            {
                Log.Main.Inform("Posting to: " + EventUrl);

                HttpClient hc = new HttpClient();
                var g = SerializationRoutines.Json.Serialize(data);
                var post_data = new StringContent(SerializationRoutines.Json.Serialize(data), Encoding.UTF8, "application/json");
                HttpResponseMessage rm = await hc.PostAsync(EventUrl, post_data);
                if (!rm.IsSuccessStatusCode)
                    Log.Main.Error2("Post: " + rm.ReasonPhrase);

                //if (rm.Content != null)
                //    var responseContent = await rm.Content.ReadAsStringAsync();
            }
            catch (Exception e)
            {
                Log.Main.Error(e);
            }
        }
    }
}