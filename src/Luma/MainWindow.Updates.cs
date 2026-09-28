using Luma.Updates;using System.Windows;using System.Windows.Media.Animation;using WpfApplication=System.Windows.Application;using WpfCursors=System.Windows.Input.Cursors;using WpfDoubleAnimation=System.Windows.Media.Animation.DoubleAnimation;
namespace Luma;
public partial class MainWindow
{
 private readonly IUpdateService _updates;private readonly CancellationTokenSource _updateLoopCancellation=new();private UpdateCheckResult? _readyUpdate;private bool _updatePromptBusy;private string _announcedUpdateVersion="";
 private async Task StartUpdateLoopAsync(){if(!_updates.IsConfigured||((App)WpfApplication.Current).IsPrivateSession)return;_auth.SessionChanged+=AuthChangedForUpdates;_updates.UpdateAvailable+=UpdateFound;try{while(!_updateLoopCancellation.IsCancellationRequested){await CheckUpdatesCore(false,_updateLoopCancellation.Token);await Task.Delay(TimeSpan.FromMinutes(5),_updateLoopCancellation.Token);}}catch(OperationCanceledException){}}
 private void UpdateFound(SignedUpdateManifest manifest)=>Dispatcher.Invoke(()=>{if(_announcedUpdateVersion==manifest.Version)return;_announcedUpdateVersion=manifest.Version;ShowToast(manifest.Title,$"Доступна версия {manifest.Version}. Безопасный пакет уже загружается в фоне.",false);});
 private void AuthChangedForUpdates(object? s,EventArgs e)=>Dispatcher.InvokeAsync(async()=>await CheckUpdatesCore(false,_updateLoopCancellation.Token));
 private async Task CheckUpdatesCore(bool manual,CancellationToken ct=default){if(_updatePromptBusy)return;_updatePromptBusy=true;try{var token=await _auth.GetAccessTokenAsync(ct);var r=await _updates.CheckAsync(Version.Parse(AppVersion),token,ct);if(r.Status==UpdateCheckStatus.Downloaded&&r.Manifest is not null&&r.PackagePath is not null){_readyUpdate=r;ShowUpdatePrompt(r.Manifest);return;}if(manual)ShowToast("Обновления Luma",r.Message,r.Status==UpdateCheckStatus.Failed);}catch(OperationCanceledException)when(ct.IsCancellationRequested){}finally{_updatePromptBusy=false;}}
 private async Task CheckForUpdatesManuallyAsync()=>await CheckUpdatesCore(true);
 private void ShowUpdatePrompt(SignedUpdateManifest m){var dialog=new UpdatePromptWindow(m){Owner=this};if(dialog.ShowDialog()==true)InstallReadyUpdate();}
 private void UpdateLater_Click(object s,RoutedEventArgs e)=>UpdateOverlay.Visibility=Visibility.Collapsed;
 private void UpdateNow_Click(object s,RoutedEventArgs e)=>InstallReadyUpdate();
 private void InstallReadyUpdate(){if(_readyUpdate?.PackagePath is null||_readyUpdate.Manifest is null)return;Cursor=WpfCursors.Wait;try{Save();UpdateInstaller.Start(_readyUpdate.PackagePath,_readyUpdate.Manifest);((App)WpfApplication.Current).ExitCompletely();}catch(Exception ex){App.Log(ex);Cursor=WpfCursors.Arrow;ShowToast("Не удалось начать обновление",ex.Message,true);}}
 private void StopUpdateLoop(){_auth.SessionChanged-=AuthChangedForUpdates;_updates.UpdateAvailable-=UpdateFound;if(!_updateLoopCancellation.IsCancellationRequested)_updateLoopCancellation.Cancel();}
}
