using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyAppearanceInteractionsAsync(string directory,Dictionary<string,object?> checks)
    {
        var original=OverlayAppearance.From(settings);bool wasVisible=overlay.IsVisible,wasLocked=overlay.IsLocked,prefVisible=settings.OverlayVisible;
        var position=NativeWindow.Location(overlay);double originalOpacity=overlay.Opacity;string originalCurrent=vm.Current,originalNext=vm.Next;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(100);}
        static void Hover(ComboBoxItem row)=>row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=MouseEnterEvent});
        static void Leave(ComboBoxItem row)=>row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=MouseLeaveEvent});
        try
        {
            overlay.SetLocked(true,false);overlay.Hide();settings.OverlayVisible=false;overlay.Opacity=.25;
            vm.Current="把此刻，轻轻唱成一束光。";vm.Next="让旋律停留在你身旁";
            SettingsClicked(this,new());ShowAppearance(settingsWindow);await Settle();
            var editor=appearanceWindow!;var parent=settingsWindow!;
            checks["editing.settingsAndOverlayStayEnabled"]=NativeWindow.IsWindowEnabled(NativeWindow.Handle(this)) && NativeWindow.IsWindowEnabled(NativeWindow.Handle(parent)) && NativeWindow.IsWindowEnabled(NativeWindow.Handle(overlay));
            checks["editing.lockedOverlayTemporarilyDraggable"]=overlay.IsLocked && overlay.AppearanceEditing && overlay.CanDrag && !NativeWindow.IsClickThrough(overlay);
            checks["editing.hiddenOverlayTemporarilyVisible"]=overlay.IsVisible && !settings.OverlayVisible;
            checks["editing.previewUsesFullOpacity"]=overlay.Opacity==1;
            ShowAppearance(parent);checks["editing.reusesOneEditor"]=ReferenceEquals(editor,appearanceWindow);
            var start=NativeWindow.Location(overlay);NativeWindow.Place(overlay,start.Left+46,start.Top-64);await Settle();
            double movedAnchor=overlay.CurrentScreenY;
            checks["editing.dragPositionIsRemembered"]=Math.Abs(NativeWindow.Location(overlay).Top-start.Top)>20 && Math.Abs(settings.OverlayTop!.Value-NativeWindow.Location(overlay).Top)<1;
            editor.PreviewTheme(OverlayThemes.All[0]);
            var picker=editor.FontPicker;picker.SelectedIndex=0;picker.IsDropDownOpen=true;editor.UpdateLayout();await Settle();
            string selected=picker.SelectedItem.ToString()!;
            var row1=(ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(1);var row2=(ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(2);
            string first=row1.Content.ToString()!,second=row2.Content.ToString()!;
            var specimen=Descendants<TextBlock>(row1).FirstOrDefault(t=>t.Text.Contains("浮光歌词"));
            checks["fontPreview.rowsHaveRealFontSpecimens"]=specimen is not null && specimen.FontFamily.Source.StartsWith(first+",",StringComparison.Ordinal) && specimen.Text.Contains("浮光歌词");
            Hover(row1);await Settle();
            checks["fontPreview.hoverChangesActualOverlay"]=overlay.LyricSurface.RenderedFont.StartsWith(first+",",StringComparison.Ordinal);
            checks["fontPreview.hoverChangesPanel"]=editor.PreviewSurface.RenderedFont.StartsWith(first+",",StringComparison.Ordinal);
            checks["fontPreview.hoverDoesNotSelectOrChangeSettings"]=picker.SelectedItem.ToString()==selected && settings.OverlayFont==selected;
            coordinator.SaveSettings();checks["fontPreview.otherSavesCannotPersistHover"]=store.Load().OverlayFont==selected;
            VisualVerification.Save(editor,Path.Combine(directory,"font-preview-editor.png"));
            if(picker.Template.FindName("PART_Popup",picker) is System.Windows.Controls.Primitives.Popup{Child:FrameworkElement list})VisualVerification.Save(list,Path.Combine(directory,"font-preview-list.png"));
            Leave(row1);await Settle();checks["fontPreview.leavingRestoresSelectedFont"]=overlay.LyricSurface.RenderedFont.StartsWith(selected+",",StringComparison.Ordinal);
            Hover(row1);Hover(row2);await Settle();checks["fontPreview.movingBetweenRowsUsesLatestFont"]=overlay.LyricSurface.RenderedFont.StartsWith(second+",",StringComparison.Ordinal);
            picker.IsDropDownOpen=false;await Settle();checks["fontPreview.closingListRestoresSelectedFont"]=overlay.LyricSurface.RenderedFont.StartsWith(selected+",",StringComparison.Ordinal);
            picker.IsDropDownOpen=true;await Settle();picker.SelectedIndex=1;await Settle();checks["fontPreview.selectingCommitsChosenFont"]=settings.OverlayFont==first && picker.HoveredFont is null;
            row2=(ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(2);Hover(row2);await Settle();editor.Save();await Settle();
            checks["fontPreview.saveIgnoresUnselectedHover"]=store.Load().OverlayFont==first && settings.OverlayFont==first;
            checks["editing.closeRestoresLockAndVisibility"]=!overlay.IsVisible && overlay.IsLocked && NativeWindow.IsClickThrough(overlay) && !overlay.AppearanceEditing && !overlay.GlassHostVisible;
            checks["editing.closeRestoresPriorFade"]=overlay.Opacity==.25;
            checks["editing.saveKeepsDraggedAnchor"]=Math.Abs(overlay.CurrentScreenY-movedAnchor)<3;
            var saved=OverlayAppearance.From(settings);ShowAppearance(parent);await Settle();var cancel=appearanceWindow!;
            cancel.FontPicker.SelectedIndex=2;cancel.FontPicker.IsDropDownOpen=true;await Settle();
            Hover((ComboBoxItem)cancel.FontPicker.ItemContainerGenerator.ContainerFromIndex(1));cancel.Close();await Settle();
            checks["fontPreview.cancelRestoresSavedAppearance"]=OverlayAppearance.From(settings)==saved && overlay.LyricSurface.RenderedFont.StartsWith(saved.Font+",",StringComparison.Ordinal);
            checks["editing.cancelKeepsDraggedAnchor"]=Math.Abs(overlay.CurrentScreenY-movedAnchor)<3;
            checks["editing.cancelCleansTemporaryInteraction"]=appearanceWindow is null && !overlay.AppearanceEditing && overlay.IsLocked && !overlay.IsVisible;
            parent.Close();
        }
        finally
        {
            appearanceWindow?.Close();settingsWindow?.Close();original.Apply(settings);settings.OverlayVisible=prefVisible;
            overlay.SetLocked(wasLocked,false);overlay.ApplyStyle();overlay.Opacity=originalOpacity;if(wasVisible)overlay.Show();else overlay.Hide();
            NativeWindow.Place(overlay,position.Left,position.Top);vm.Current=originalCurrent;vm.Next=originalNext;coordinator.SaveSettings();
        }
    }
}
