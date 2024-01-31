using BlockifyLauncher.Core.ViewModel;
using BlockifyLauncher.MVVM.ViewModel.Pages;

namespace BlockifyLauncher.MVVM.ViewModel
{
    public class CurrentViewModel : ObservableObject
    {
        #region model switching command.
        public RelayCommand MainCommand { get; set; }
        public RelayCommand SettingCommand { get; set; }
        #endregion

        #region view model.
        public MainModel MainVM { get; set; }
        public SettingModel SettingVM { get; set; }
        #endregion

        private object _currntView;
        public object CurrentView
        {
            get
            {
                return _currntView;
            }
            set
            {
                _currntView = value;
                OnPropentyChanged();
            }
        }

        private void LoadingVM()
        {
            MainVM = new MainModel();
            SettingVM = new SettingModel();
        }

        public CurrentViewModel()
        {
            LoadingVM();
            this.CurrentView = this.MainVM;

            MainCommand = new RelayCommand(o => { CurrentView = MainVM; });
            SettingCommand = new RelayCommand(o => { CurrentView = SettingVM; });
        }
    }
}
