using CommunityToolkit.Mvvm.ComponentModel;
namespace ShopBilling.Wpf.ViewModels.Pages;
public abstract partial class PageViewModel : ObservableObject
{
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isBusy;
 [ObservableProperty] private bool isError;
 [ObservableProperty] private string statusMessage = "Ready.";
 public bool CanEdit => !IsBusy;
 public async Task ExecuteAsync(Func<Task> action, string? success = null)
 {
  if (IsBusy) return;
  IsBusy = true; IsError = false;
  try { await action(); if (success is not null) StatusMessage = success; }
  catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
  finally { IsBusy = false; }
 }
}
