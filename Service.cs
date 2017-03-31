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
            process_min_address = (long)si.minimumApplicationAddress;
            process_max_address = (long)si.maximumApplicationAddress;
        }
        static readonly long process_min_address;
        static readonly long process_max_address;

        public delegate void OnStateChanged();
        public static event OnStateChanged StateChanged = null;

        public static bool Running
        {
            set
            {
                if (value)
                {
                    if (monitor_t == null || !monitor_t.IsAlive)
                        monitor_t = Cliver.ThreadRoutines.StartTry(
                            monitor,
                            null,
                            () =>
                            {
                                monitor_t = null;
                                StateChanged?.BeginInvoke(null, null);
                            }
                            );
                }
                else
                {
                    if (monitor_t != null && monitor_t.IsAlive)
                        monitor_t.Abort();
                    monitor_t = null;
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
        static Encoding Encoding;

        static void monitor()
        {
            StateChanged?.BeginInvoke(null, null);

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
            Encoding = System.Text.Encoding.GetEncoding(Settings.General.EncodingCodePage);
            if (Encoding == null)
                throw new Exception("Encoding is not specified.");

            while (monitor_t != null)
            {
                DateTime next_check_time = DateTime.Now.AddSeconds(CheckPeriodInSecs);
                process(ProcessName);
                if (next_check_time > DateTime.Now)
                    Thread.Sleep(next_check_time - DateTime.Now);
            }
        }

        static void process(string process_name)
        {
            Process[] ps = Process.GetProcessesByName(process_name);
            if (ps.Length < 1)
                Log.Main.Warning("No process '" + process_name + "' exists.");
            else
            {
                foreach (Process p in ps)
                {
                    Log.Main.Inform("Dumping process '" + p.ProcessName + "', id: " + p.Id);
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
                                    LogMessage.Exit("Despite the app is running with elevated privileges, it still cannot EnterDebugMode. Please fix the problem before using the app.");
                                LogMessage.Inform(ProgramRoutines.GetAppName() + " needs administatrator privileges to monitor process '" + process_name + "'. So it will restart now and ask for elevated privileges.");
                                ControlRoutines.InvokeFromUiThread((Action)delegate { ProcessRoutines.Restart(true); });
                            }
                            else
                                throw e;
                                //throw new Exception("OpenProcess failed:" + Win32Routines.GetLastErrorString());
                        }
                    }

                    Win64.MEMORY_BASIC_INFORMATION mbi;
                    uint mbi_size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win64.MEMORY_BASIC_INFORMATION));
                    List<List<string>> matches = new List<List<string>>();
                    string text0 = "";
                    for (long address = process_min_address; address < process_max_address; address += mbi.RegionSize)
                    {
                        if (1 > Win64.VirtualQueryEx(ph, new IntPtr(address), out mbi, mbi_size))
                            throw new Exception("VirtualQueryEx failed:" + Win32Routines.GetLastErrorString());
                        if ((mbi.State & Win32.MemoryState.MEM_COMMIT) != Win32.MemoryState.MEM_COMMIT)
                            continue;
                        if ((mbi.Protect & Win32.MemoryProtection.PAGE_NOACCESS) == Win32.MemoryProtection.PAGE_NOACCESS)
                            continue;
                        if ((mbi.Protect & Win32.MemoryProtection.PAGE_GUARD) == Win32.MemoryProtection.PAGE_GUARD)
                            continue;
                        byte[] bs = new byte[mbi.RegionSize];
                        int bytes_count = 0;
                        if (!Win64.ReadProcessMemory((int)ph, mbi.BaseAddress, bs, mbi.RegionSize, ref bytes_count))
                            Log.Main.Error2("ReadProcessMemory failed:" + Win32Routines.GetLastErrorString());
                        string text = text0 + Encoding.GetString(bs, 0, bytes_count);
                        int last_match_end;
                        parse(text, ref matches, out last_match_end);
                        int text0_length = BUFFER_PASS_SIZE;
                        if (last_match_end > 0)
                        {
                            text0_length = text.Length - last_match_end;
                            if (text0_length > BUFFER_PASS_SIZE)
                                text0_length = BUFFER_PASS_SIZE;
                        }
                        text0 = text.Substring(text.Length - text0_length);
                    }
                    //if (matches.Count > 0)
                    {
                        Log.Main.Write("MATCHES:\r\n" + SerializationRoutines.Json.Serialize(matches));
                        post(new { Process = process_name, Regex = DumpRegex, Encoding = Encoding, Matches = matches });
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
                Log.Main.Inform("Posting to " + EventUrl);

                HttpClient hc = new HttpClient();
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