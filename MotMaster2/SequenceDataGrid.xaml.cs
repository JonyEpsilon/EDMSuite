﻿using MOTMaster2.SequenceData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Markup;


namespace MOTMaster2
{
    /// <summary>
    /// Interaction logic for SequenceDataGrid.xaml
    /// </summary>
    public partial class SequenceDataGrid : UserControl
    {

        public SequenceDataGrid()
        {
            InitializeComponent();
            sequenceDataGrid.DataContext = new SequenceStepViewModel();
        }

        public void UpdateSequenceData()
        {
            SequenceStepViewModel model = (SequenceStepViewModel)sequenceDataGrid.DataContext;
            model.SequenceSteps.Clear();
            foreach (SequenceStep step in Controller.sequenceData.Steps) model.SequenceSteps.Add(step);
        }
        private void sequenceDataGrid_AutoGeneratedColumns(object sender, EventArgs e)
        {
            var dg = sender as DataGrid;
            //These hide the columns that by default display the dictionaries corresponding to the analog and digital channel types
            dg.Columns[6].Visibility = Visibility.Collapsed;
            dg.Columns[7].Visibility = Visibility.Collapsed;

            var first = dg.ItemsSource.Cast<object>().FirstOrDefault() as SequenceStep;
            if (first == null) return;
            var names = first.AnalogValueTypes.Keys;
            Style analogStyle = new Style(typeof(ComboBox));
     
            analogStyle.Setters.Add(new EventSetter() { Event = ComboBox.SelectionChangedEvent, Handler = new SelectionChangedEventHandler(this.sequenceDataGrid_AnalogValueChanged)});
            foreach (var name in names)
            {
                DataGridComboBoxColumn col = new DataGridComboBoxColumn { Header = name,EditingElementStyle = analogStyle};
  
                var resource = this.FindResource("analogProvider");
                BindingOperations.SetBinding(col, DataGridComboBoxColumn.ItemsSourceProperty, new Binding() {Source = resource });
                col.SelectedItemBinding = new Binding("AnalogValueTypes[" + name + "]");
                dg.Columns.Add(col);
                
       
            }
        /*    var dignames = first.DigitalValueTypes.Keys;
            foreach (var name in dignames)
            {
                DataGridCheckBoxColumn col = new DataGridCheckBoxColumn { Header = name };
                col.Binding = new Binding() { Path = new PropertyPath("DigitalValueTypes[" + name + "].Value")};
                dg.Columns.Add(col);
            }*/
            var dignames = first.DigitalValueTypes.Keys;
           // Style digitalStyle = (Style)this.Resources["BackgroundCheckBoxStyle"];
            Style digitalStyle = new Style();
            digitalStyle.Setters.Add(new EventSetter() { Event = CheckBox.CheckedEvent, Handler = new RoutedEventHandler(this.sequenceDataGrid_chkDigitalChecked) });
            digitalStyle.Setters.Add(new EventSetter() { Event = CheckBox.UncheckedEvent, Handler = new RoutedEventHandler(this.sequenceDataGrid_chkDigitalChecked) });

            foreach (var name in dignames)
            {
                //var resource = this.FindResource("digitalProvider");

                DataGridCheckBoxColumn col = new DataGridCheckBoxColumn { Header = name};
                col.Binding = new Binding() { Path = new PropertyPath("DigitalValueTypes[" + name + "].Value") };
                col.ElementStyle = digitalStyle;
                dg.Columns.Add(col);
            }
            dg.FrozenColumnCount = 5;
        }

        //If the properties of the SequenceData are changed, this will be called
        private void sequenceDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            var dg = sender as DataGrid;
            if (dg.CurrentItem.GetType() == null){ return;}
            List<SequenceStep> first = new List<SequenceStep>((ObservableCollection<SequenceStep>) dg.ItemsSource);
            SequenceStepViewModel model = (SequenceStepViewModel)sequenceDataGrid.DataContext;
            if (dg.CurrentItem.GetType() == typeof(SequenceStep)) model.SelectedSequenceStep = (SequenceStep)dg.CurrentItem;
            if (Controller.sequenceData != null) Controller.sequenceData.Steps = first;
        }

        private void sequenceDataGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            //Controller ctrl;
        }
        private void sequenceDataGrid_AnalogValueChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (sequenceDataGrid.CurrentColumn == null) return;
            string channelName = (string)sequenceDataGrid.CurrentColumn.Header;
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AnalogChannelSelector c = (AnalogChannelSelector)e.AddedItems[0];
                if (c != AnalogChannelSelector.Continue)
                {
                    //Only raises an event if the analog channel is being changed to something other than continue
                    SequenceStepViewModel model = (SequenceStepViewModel)sequenceDataGrid.DataContext;
                    model.SelectedAnalogChannel = new KeyValuePair<string, AnalogChannelSelector>(channelName, c);
                    OnChangedAnalogChannelCell(sender,e);
                }
            }
        }
        private void sequenceDataGrid_chkDigitalChecked(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("654654");
            var cell = sender as CheckBox;
            //cell.Background 
            Console.WriteLine(sender.ToString());
            if (cell.IsChecked.Value) cell.Background = new SolidColorBrush(Colors.Red);
            else cell.Background = new SolidColorBrush(Colors.Black);
        }

        public delegate void ChangedAnalogChannelCellHandler(object sender, SelectionChangedEventArgs e);
        public event ChangedAnalogChannelCellHandler ChangedAnalogChannelCell;

        protected void OnChangedAnalogChannelCell(object sender, SelectionChangedEventArgs e)
        {
            if (ChangedAnalogChannelCell != null) ChangedAnalogChannelCell(sender, e);
        }

        public delegate void ChangedRS232CellHandler(object sender, DataGridBeginningEditEventArgs e);
        public event ChangedRS232CellHandler ChangedRS232Cell;

        protected void OnChangedRS232Cell(object sender, DataGridBeginningEditEventArgs e)
        {
            if (ChangedRS232Cell != null) ChangedRS232Cell(sender, e);
        }



 /*       public static readonly RoutedEvent ChangedAnalogChannelCellEvent = EventManager.RegisterRoutedEvent("ChangedAnalogChannelCellEvent", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ComboBox));

        public static readonly RoutedEvent ChangedRS232CellEvent = EventManager.RegisterRoutedEvent("ChangedRS232CellEvent", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CheckBox));
        
        public event RoutedEventHandler ChangedAnalogCell
        {
            add { AddHandler(ChangedAnalogChannelCellEvent, value); }
            remove { RemoveHandler(ChangedAnalogChannelCellEvent, value); }
        }
        public event RoutedEventHandler ChangedRS232Event
        {
            add { AddHandler(ChangedRS232CellEvent, value); }
            remove { RemoveHandler(ChangedRS232CellEvent, value); }
        }*/

        private void sequenceDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var dg = sender as DataGrid;
            if ((string)dg.CurrentCell.Column.Header == "RS232Commands")
            {
                SequenceStepViewModel model = (SequenceStepViewModel)sequenceDataGrid.DataContext;
                model.RS232Enabled = !model.RS232Enabled;
                OnChangedRS232Cell(sender, e);
            }
            else
            { return; }

        }

        private void sequenceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Console.WriteLine("Clicked: " + sender.ToString());
        }

    }

    [ValueConversion(typeof(bool),typeof(Brush))]
    public class BooleanToBrushConverter : MarkupExtension,IValueConverter
    {
        private static BooleanToBrushConverter _converter = null;
        public BooleanToBrushConverter()
        {

        }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Brushes.Transparent;

            Brush[] brushes = parameter as Brush[];
            if (brushes == null)
                return Brushes.Red;

            bool isTrue;
            bool.TryParse(value.ToString(), out isTrue);

            if (isTrue)
            {
                var brush = (SolidColorBrush)brushes[0];
                return brush ?? Brushes.Transparent;
            }
            else
            {
                var brush = (SolidColorBrush)brushes[1];
                return brush ?? Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _converter ?? (_converter = new BooleanToBrushConverter());
        }
    }
}

