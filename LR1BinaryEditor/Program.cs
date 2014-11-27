using System;
using System.IO;
using System.Windows.Forms;

namespace LR1BinaryEditor {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args) {
            try {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainFormScintilla(args));
            }
            catch (Exception ex) {
                LogError(ex);
            }
        }

        static void LogError(Exception ex) {

            MessageBox.Show("An error of type `" + ex.GetType().ToString() + "` has occurred, with the following message:\n`" + ex.Message + "`", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Stop);

            DateTime dt = DateTime.Now;
            string filename = dt.Year.ToString("0000") + "." + dt.Month.ToString("00") + "." + dt.Day.ToString("00") + "." + dt.Hour.ToString("00") + "." + dt.Minute.ToString("00") + "." + dt.Second.ToString("00");
            using (StreamWriter sw = new StreamWriter(filename + ".log")) {
                sw.WriteLine("Type: " + ex.GetType().ToString());
                sw.WriteLine("Message: " + ex.Message);
                sw.WriteLine("Stack Trace: " + ex.StackTrace);
            }
        }
    }
}