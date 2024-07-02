using BlockifyLauncher.Core.ViewModel;
using BlockifyLauncher.MVVM.ViewModel.Pages;

namespace BlockifyLauncher.MVVM.ViewModel
{
    public class CurrentViewModel : ObservableObject
    {

        #region model switching command.
        public RelayCommand MainCommand { get; set; }
        public RelayCommand SettingCommand { get; set; }
        public RelayCommand AccountCommand { get; set; }
        public RelayCommand LastPageCommand { get; set; }
        #endregion

        #region view model.
        public MainModel MainVM { get; set; }
        public SettingModel SettingVM { get; set; }
        public AccountModel AccountVM { get; set; }
        #endregion

        #region lastPage.
        private object _lastView;
        public object LastView
        {
            get
            {
                return _lastView;
            }
            set
            {
                _lastView = value;
            }
        }
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
            AccountVM = new AccountModel();
        }

        private void GoLastPage()
        {
            this.CurrentView = this.LastView;
        }

        public CurrentViewModel()
        {
            LoadingVM();
            this.CurrentView = this.MainVM;

            LastPageCommand = new RelayCommand(o => GoLastPage());

            MainCommand = new RelayCommand(o => { CurrentView = MainVM; });
            SettingCommand = new RelayCommand(o => { CurrentView = SettingVM; });

            AccountCommand = new RelayCommand(o => {
                LastView = CurrentView;
                CurrentView = AccountVM;
            });

        }
    }
}
