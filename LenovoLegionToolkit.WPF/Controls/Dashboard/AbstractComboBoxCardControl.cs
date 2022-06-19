﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.WPF.Extensions;
using WPFUI.Common;
using WPFUI.Controls;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard
{
    public abstract class AbstractComboBoxCardControl<T> : AbstractRefreshingControl where T : struct
    {
        private readonly IFeature<T> _feature = IoCContainer.Resolve<IFeature<T>>();

        private readonly CardControl _cardControl = new();
        protected readonly ComboBox _comboBox = new();

        public SymbolRegular Icon
        {
            get => _cardControl.Icon;
            set => _cardControl.Icon = value;
        }

        public string Title
        {
            get => _cardControl.Title;
            set => _cardControl.Title = value;
        }

        public string Subtitle
        {
            get => _cardControl.Subtitle;
            set => _cardControl.Subtitle = value;
        }

        public AbstractComboBoxCardControl() => InitializeComponent();

        private void InitializeComponent()
        {
            _comboBox.SelectionChanged += ComboBox_SelectionChanged;
            _comboBox.Width = 150;
            _comboBox.Visibility = Visibility.Hidden;

            _cardControl.Margin = new(0, 0, 0, 8);
            _cardControl.Content = _comboBox;

            Content = _cardControl;
        }

        private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await OnStateChange(_comboBox, _feature);

        protected override async Task OnRefreshAsync()
        {
            var items = await _feature.GetAllStatesAsync();
            var selectedItem = await _feature.GetStateAsync();

            static string displayName(T value)
            {
                if (value is IDisplayName dn)
                    return dn.DisplayName;
                if (value is Enum e)
                    return e.GetDisplayName();
                return value.ToString() ?? throw new InvalidOperationException("Unsupported type");
            }

            _comboBox.SetItems(items, selectedItem, displayName);
            _comboBox.IsEnabled = items.Any();
        }

        protected override void OnFinishedLoading()
        {
            _comboBox.Visibility = Visibility.Visible;

            MessagingCenter.Subscribe<T>(this, () => Dispatcher.InvokeTask(RefreshAsync));
        }

        protected virtual async Task OnStateChange(ComboBox comboBox, IFeature<T> feature)
        {
            if (IsRefreshing)
                return;

            if (!comboBox.TryGetSelectedItem(out T selectedState))
                return;

            T currentState = await feature.GetStateAsync();

            if (selectedState.Equals(currentState))
                return;

            await feature.SetStateAsync(selectedState);
        }
    }
}