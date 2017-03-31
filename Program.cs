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
using System.Net.Mail;
using Cliver;
using System.Configuration;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Windows.Input;
using GlobalHotKey;


namespace Cliver.RamMonitor
{
    public class Program
    {
        static Program()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs args)
            {
                Exception e = (Exception)args.ExceptionObject;
                Message.Error(e);
                Application.Exit();
            };

            hotKeyManager = new HotKeyManager();
            var hotKey = hotKeyManager.Register(Key.F1, ModifierKeys.Alt);
            hotKeyManager.KeyPressed += delegate (object sender, KeyPressedEventArgs e)
            {
                if (!Message.YesNo("Do you want to terminate " + ProgramRoutines.GetAppName() + "?"))
                    return;
                Log.Main.Exit2("Keys pressed.");
            };
        }
        
        static readonly HotKeyManager hotKeyManager;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                Log.Initialize(Log.Mode.ONLY_LOG);
                //Cliver.Config.Initialize(new string[] { "General" });
                Cliver.Config.Reload();

                InternetDateTime.CHECK_TEST_PERIOD_VALIDITY(2017, 4, 11);

                ProcessRoutines.RunSingleProcessOnly();

                Dictionary<string, string> clps = ProgramRoutines.GetCommandLineParameters();
                string v;
                if (clps.TryGetValue("ProcessName", out v))
                    Settings.General.ProcessName = v;
                if (clps.TryGetValue("DumpRegex", out v))
                    Settings.General.DumpRegex = new Regex(v);
                if (clps.TryGetValue("EventUrl", out v))
                    Settings.General.EventUrl = v;
                if (clps.TryGetValue("CheckPeriodInSecs", out v))
                    Settings.General.CheckPeriodInSecs = uint.Parse(v);
                //Settings.General.Save();

                Service.Running = true;

                Application.Run(SysTray.This);
            }
            catch (Exception e)
            {
                Message.Error(e);
            }
            finally
            {
                Environment.Exit(0);
            }
        }
    }
}