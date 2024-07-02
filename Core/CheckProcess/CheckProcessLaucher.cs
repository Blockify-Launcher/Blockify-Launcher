using System.Diagnostics;
using System.Management;

namespace BlockifyLauncher.Core.CheckProcess
{
    public class CheckProcessLaucher
    {
        // Process object.
        private ManagementObject[] _object;

        private string _targetProcessName;
        private Process _process;

        public long[] informationPIDProcess;

        public CheckProcessLaucher()
        {
            _targetProcessName = "BlockifyLauncher"; // TODO : edit automatization
            _process = new Process();

            informationPIDProcess = new long[0];
        }

        public void UpdateProcessing()
        {
            _object = ConvertToManagementObjectArray(
                    GetCommandLine()
                );

            informationPIDProcess = new long[_object.Length];
        }

        private ManagementObjectCollection GetCommandLine()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher
                    ($"SELECT ProcessId, CommandLine FROM Win32_Process WHERE CommandLine LIKE '%{_targetProcessName}%' OR Name LIKE '%{_targetProcessName}%'"))
                    return searcher.Get();
            }
            catch { return null; }
            return null;
        }

        private ManagementObject[] ConvertToManagementObjectArray(ManagementObjectCollection collection) =>
            collection.Cast<ManagementObject>().ToArray();
    }
}
