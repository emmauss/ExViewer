﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Data.Json;
using Windows.UI.Core;
using Windows.UI.Xaml.Media.Imaging;

//“空白页”项模板在 http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 上有介绍

namespace ExViewer.Views
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : UserControl
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        SystemNavigationManager manager;

        private void btn_pane_Click(object sender, RoutedEventArgs e)
        {
            sv_root.IsPaneOpen = !sv_root.IsPaneOpen;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            manager = SystemNavigationManager.GetForCurrentView();
            manager.BackRequested += Manager_BackRequested;
            fm_inner.Navigate(typeof(SearchPage));
        }

        private void Manager_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if(fm_inner.CanGoBack)
            {
                fm_inner.GoBack();
                e.Handled = true;
            }
        }

        private void fm_inner_Navigated(object sender, NavigationEventArgs e)
        {
            var page = e.Content as IMainPageController;
            if(page != null)
                page.CommandExecuted += Page_CommandExecuted;
            if(fm_inner.CanGoBack)
                manager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            else
                manager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
        }

        private void fm_inner_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            var page = fm_inner.Content as IMainPageController;
            if(page != null)
                page.CommandExecuted -= Page_CommandExecuted;
        }

        private void Page_CommandExecuted(object sender, MainPageControlCommand e)
        {
            switch(e)
            {
            case MainPageControlCommand.SwitchSplitView:
                sv_root.IsPaneOpen = !sv_root.IsPaneOpen;
                break;
            default:
                break;
            }
        }
    }

    internal class CategoryToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var cata = (ExClient.Category)value;
            var uri = new Uri($"ms-appx:///CategoryImage/{cata.ToString()}.png");
            return new BitmapImage(uri);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal class StringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.Format(parameter.ToString(), value.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}