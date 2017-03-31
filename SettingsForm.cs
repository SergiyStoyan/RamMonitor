using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cliver.RamMonitor
{
    public partial class SettingsForm : Form//BaseForm// 
    {
        public SettingsForm()
        {
            InitializeComponent();

            FormClosed += delegate
              {
                  sf = null;
              };

            ProcessName.Text = Settings.General.ProcessName;
            DumpRegex.Text = Settings.General.DumpRegex.ToString();
            EventUrl.Text = Settings.General.EventUrl;
            CheckPeriodInSecs.Text = Settings.General.CheckPeriodInSecs.ToString();

            //Encoding.DisplayMember = "Text";
            //Encoding.ValueMember = "CodePage";
            //List<EncodingItem> its = new List<EncodingItem>();
            //foreach (EncodingInfo ei in System.Text.Encoding.GetEncodings())
            //    its.Add(new EncodingItem { Text = ei.Name, CodePage = ei.GetEncoding().CodePage });
            //Encoding.Items.AddRange(its.ToArray());
            //EncodingItem si = its.Where(i => i.CodePage == Settings.General.EncodingCodePage).First();
            //if (si != null)
            //    Encoding.SelectedIndex = its.IndexOf(si);
            
            foreach (System.Windows.Input.Key k in Enum.GetValues(typeof(System.Windows.Input.Key)))
                TerminatingKey.Items.Add(k);
            TerminatingKey.SelectedItem = Settings.General.TerminatingKey;
            
            foreach (System.Windows.Input.ModifierKeys k in Enum.GetValues(typeof(System.Windows.Input.ModifierKeys)))
                TerminatingModifierKey1.Items.Add(k);
            TerminatingModifierKey1.SelectedItem = Settings.General.TerminatingModifierKey1;
            
            foreach (System.Windows.Input.ModifierKeys k in Enum.GetValues(typeof(System.Windows.Input.ModifierKeys)))
                TerminatingModifierKey2.Items.Add(k);
            TerminatingModifierKey2.SelectedItem = Settings.General.TerminatingModifierKey2;
        }

        //public class EncodingItem
        //{
        //    public string Text { get; set; }
        //    public int CodePage { get; set; }
        //}

        static public void Open()
        {
            if (sf == null)
                sf = new SettingsForm();
            sf.Show();
            sf.Activate();
        }
        static SettingsForm sf = null;

        private void bCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void bOk_Click(object sender, EventArgs e)
        {
            try
            {
                Settings.General.ProcessName = ProcessName.Text;
                Settings.General.DumpRegex = new System.Text.RegularExpressions.Regex(DumpRegex.Text);
                Settings.General.EventUrl = EventUrl.Text;
                Settings.General.CheckPeriodInSecs = uint.Parse(CheckPeriodInSecs.Text);
                //Settings.General.EncodingCodePage = ((EncodingItem)Encoding.SelectedItem).CodePage;
                Settings.General.TerminatingKey = (System.Windows.Input.Key)TerminatingKey.SelectedItem;
                Settings.General.TerminatingModifierKey1 = (System.Windows.Input.ModifierKeys)TerminatingModifierKey1.SelectedItem;
                Settings.General.TerminatingModifierKey2 = (System.Windows.Input.ModifierKeys)TerminatingModifierKey2.SelectedItem;
                Settings.General.Save();

                Program.SetTerminatingKeys();

                Close();

                bool running = Service.Running;
                Service.Running = false;
                Service.Running = running;
            }
            catch (Exception ex)
            {
                Message.Exclaim(ex.Message);
            }
        }
    }
}