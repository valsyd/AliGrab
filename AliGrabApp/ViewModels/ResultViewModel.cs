﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Entity;
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AliGrabApp.Models;
using Microsoft.Win32;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace AliGrabApp.ViewModels
{
    public delegate void DbChangedHandler();
    public delegate void ProgressHandler(ProgressBarModel pb);

    public class ResultViewModel : ViewModelBase
    {
        private bool _canExecute;
        private ICommand _saveDbCommand;
        private ICommand _exportCommand;
        private BackgroundWorker _bw1 = new BackgroundWorker();
        private BackgroundWorker _bw2 = new BackgroundWorker();

        public ObservableCollection<AliItem> AliItems { get; set; }
        public ProgressBarModel ProgressBar { get; set; }
        public ButtonModel Buttons { get; set; }
        public string CollectionTitle { get; set; }


        public static event DbChangedHandler OnDbChanged;
        public static event ProgressHandler OnProgress;

        public ResultViewModel()
        {
            // Init
            ProgressBar = new ProgressBarModel {Visibility = Visibility.Hidden};
            Buttons = new ButtonModel {IsEnabled = true};
            AliItems = new ObservableCollection<AliItem>();
            // Commands status
            _canExecute = true;
            // Background worker1 settings
            _bw1.WorkerReportsProgress = true;
            _bw1.WorkerSupportsCancellation = true;
            _bw1.ProgressChanged += ProgressChanged;
            _bw1.DoWork += DoSaveDb;
            _bw1.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
            // Background worker2 settings
            _bw2.WorkerReportsProgress = true;
            _bw2.WorkerSupportsCancellation = true;
            _bw2.ProgressChanged += ProgressChanged;
            _bw2.DoWork += DoExport;
            _bw2.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
            // Subscribe on all items grabbed event
            SearchViewModel.OnItemsGrabbed += ShowResult;
            // Subscribe events
            ExplorerViewModel.OnItemsOpened += ShowResult;
            StatusViewModel.OnTaskCanceled += CancelWork;
            StatusViewModel.OnTaskStarted += () => { Buttons.IsEnabled = false; };
            StatusViewModel.OnTaskFinished += () => { Buttons.IsEnabled = true; };
        }


        public void OnWindowClosed(object sender, EventArgs e)
        {
            // background worker cancalation
            _bw1.CancelAsync();
            _bw2.CancelAsync();
        }

        private void CancelWork()
        {
            // background worker cancalation
            _bw1.CancelAsync();
            _bw2.CancelAsync();
        }

        private void ShowResult(ObservableCollection<AliItem> items)
        {
            AliItems.Clear();
            foreach (var item in items)
            {                
                AliItems.Add(new AliItem
                {
                    No = item.No,
                    Image = item.Image,
                    Title = item.Title,
                    Price = item.Price,
                    PriceCurrency = item.PriceCurrency,
                    Unit = item.Unit,
                    Seller = item.Seller,
                    Description = item.Description,
                    Link = item.Link
                });
            }
        }

        public ICommand SaveDbCommand
        {
            get
            {
                return _saveDbCommand ?? (_saveDbCommand = new Commands.CommandHandler(() => Save(), _canExecute));
            }
        }

        public ICommand ExportCommand
        {
            get
            {
                return _exportCommand ?? (_exportCommand = new Commands.CommandHandler(() => _bw2.RunWorkerAsync(), _canExecute));
            }
        }

        private void Save()
        {
            // Check for empty list
            if (AliItems.Count == 0)
            {
                // Show alert
                MessageBox.Show("Nothing to save!",
                                "Info",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }
            // Show save dialog
            var inputDialog = new InputDialog("Please enter the title:", "Collection");
            var result = inputDialog.ShowDialog();
            if (result != true) return;
            CollectionTitle = inputDialog.Answer;
            _bw1.RunWorkerAsync();
        }

        private void DoSaveDb(object sender, DoWorkEventArgs e)
        {
            // Show progress bar
            ProgressBar.Value = 0;
            ProgressBar.Content = "";
            ProgressBar.Visibility = Visibility.Visible;
            // Send current progress to status view
            OnProgress?.Invoke(ProgressBar);
            // Disable buttons
            Buttons.IsEnabled = false;
            // Show progressbar
            ProgressBar.Visibility = Visibility.Visible;

            try
            {
                using (AliContext db = new AliContext())
                {
                    var group = new AliGroupModel();
                    group.Name = CollectionTitle;
                    group.Created = DateTime.Now;
                    group.Items = new ObservableCollection<AliItemModel>();

                    // Add all items
                    int counter = 0;
                    int itemsCount = AliItems.Count;
                    foreach (var item in AliItems)
                    {
                        var itemModel = new AliItemModel
                        {
                            Title = item.Title,
                            Price = item.Price,
                            PriceCurrency = item.PriceCurrency,
                            Unit = item.Unit,
                            Seller = item.Seller,
                            Link = item.Link,
                            Description = item.Description,
                            Image = item.Image
                        };
                        group.Items.Add(itemModel);

                        // Set progress bar value
                        counter++;
                        int percent = (int)(Convert.ToDouble(counter) / Convert.ToDouble(itemsCount) * 100);
                        _bw1.ReportProgress(percent, String.Format("Saving item(s) {0} of {1}", counter, itemsCount));
                    }
                    db.Groups.Add(group);
                    db.SaveChanges();
                }

                // Fire database changed event to refresh main window
                OnDbChanged?.Invoke();

                // Show alert
                MessageBox.Show("Items successfully saved!",
                                "Info",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (EntityException ex)
            {
                // Show message box
                MessageBox.Show("Unable to connect to database. \n \n" +
                                "Check the database instance MS SQL Server LocalDB on your computer.\n \n" +
                                ex.Message,
                                "Error!",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message,
                                "Error!",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void DoExport(object sender, DoWorkEventArgs e)
        {
            var exportStoped = false;

            // Disable buttons
            Buttons.IsEnabled = false;

            // Check for empty list
            if (AliItems.Count == 0)
            {
                // Show alert
                MessageBox.Show("Nothing to export!",
                                "Info",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            try
            {
                // Show save file dialog
                var fileDialog = new SaveFileDialog
                {
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                    Filter = "Microsoft Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                    Title = "Export to file"
                };

                var result = fileDialog.ShowDialog();
                if (result != true) return;
                var file = new FileStream(fileDialog.FileName, FileMode.Create);

                // Show progress bar
                ProgressBar.Value = 0;
                ProgressBar.Content = "";
                ProgressBar.Visibility = Visibility.Visible;
                // Send current progress to status view
                OnProgress?.Invoke(ProgressBar);

                using (var ep = new ExcelPackage(file))
                {

                    ep.Workbook.Worksheets.Add("AliExpress items");
                    var ws = ep.Workbook.Worksheets[1];

                    // Create header
                    ws.Cells[1, 2].Value = "Image";
                    ws.Cells[1, 1].Value = "No";
                    ws.Cells[1, 3].Value = "Title";
                    ws.Cells[1, 4].Value = "Price";
                    ws.Cells[1, 5].Value = "Currency";
                    ws.Cells[1, 6].Value = "Unit";
                    ws.Cells[1, 7].Value = "Seller";
                    ws.Cells[1, 8].Value = "Description";
                    ws.Cells[1, 9].Value = "Url";

                    // Set column width
                    ws.Column(2).Width = 20.0;
                    ws.Column(1).Width = 12.0;
                    ws.Column(3).Width = 60.0;
                    ws.Column(4).Width = 10.0;
                    ws.Column(5).Width = 10.0;
                    ws.Column(6).Width = 10.0;
                    ws.Column(7).Width = 15.0;
                    ws.Column(8).Width = 40.0;
                    ws.Column(9).Width = 80.0;


                    // Set alignment
                    for (int i = 1; i <= 9; i++)
                    {
                        if (i == 2) { continue; }
                        ws.Column(i).Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        ws.Column(i).Style.HorizontalAlignment =
                            (i != 3 && i < 8) ? ExcelHorizontalAlignment.Center : ExcelHorizontalAlignment.Left;
                    }


                    // Add all items
                    int rowNo = 2;
                    int counter = 0;
                    int itemsCount = AliItems.Count;
                    foreach (var item in AliItems)
                    {
                        // Get image
                        using (var ms = new MemoryStream(item.Image))
                        {
                            using (var img = System.Drawing.Image.FromStream(ms))
                            {
                                if (img != null)
                                {
                                    var cellHeight = 100.0;                                         
                                                                                                   
                                    ws.Row(rowNo).Height = cellHeight;

                                    // Add picture to cell
                                    var pic = ws.Drawings.AddPicture("Picture" + rowNo, img);
                                    // Position picture on desired column
                                    pic.From.Column = 1;
                                    pic.From.Row = rowNo - 1;
                                    // Set picture size to fit inside the cell
                                    int imageWidth = (int)(img.Width * cellHeight / img.Height);
                                    int imageHeight = (int)cellHeight;
                                    pic.SetSize(imageWidth, imageHeight);
                                }
                            }
                        }

                        ws.Cells[rowNo, 1].Style.WrapText = true;
                        ws.Cells[rowNo, 3].Style.WrapText = true;
                        ws.Cells[rowNo, 4].Style.WrapText = true;
                        ws.Cells[rowNo, 5].Style.WrapText = true;
                        ws.Cells[rowNo, 6].Style.WrapText = true;
                        ws.Cells[rowNo, 7].Style.WrapText = true;
                        ws.Cells[rowNo, 8].Style.WrapText = true;
                        ws.Cells[rowNo, 9].Style.WrapText = true;

                        ws.Cells[rowNo, 1].Value = item.No;
                        ws.Cells[rowNo, 3].Value = item.Title;
                        ws.Cells[rowNo, 4].Value = item.Price;
                        ws.Cells[rowNo, 5].Value = item.PriceCurrency;
                        ws.Cells[rowNo, 6].Value = item.Unit;
                        ws.Cells[rowNo, 7].Value = item.Seller;
                        ws.Cells[rowNo, 8].Value = item.Description;
                        ws.Cells[rowNo, 9].Value = item.Link;
                        rowNo++;

                        // Set progress bar value
                        counter++;
                        int percent = (int)(Convert.ToDouble(counter) / Convert.ToDouble(itemsCount) * 100);
                        _bw2.ReportProgress(percent, String.Format("Exporting to Excel item(s) {0} of {1}", counter, itemsCount));

                        // Check for background worker cancelation
                        if (_bw2.CancellationPending)
                        {
                            e.Cancel = true;
                            // Finalize export
                            ep.Save();
                            file.Flush();
                            file.Close();

                            // Set flag
                            exportStoped = true;

                            // Show alert
                            MessageBox.Show("Export was canceled!",
                                            "Info",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Information);

                            return;
                        }
                    }
                    ep.Save();


                }
                // Finalize export
                file.Flush();
                file.Close();

                // Show alert
                if (!exportStoped)
                {
                    MessageBox.Show("Export was successfully finished!",
                                "Info",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                }
                
            }
            catch (Exception ex)
            {
                MessageBox.Show("Imposible to export data to an Excel file. \n \n" +
                                "- Maybe you don't have permission to write the data in the selected " +
                                "folder. Try saving the file to a different directory." +
                                "- Perhaps the data is corrupted. Try to repeat your search request. \n" +
                                ex.Message,
                                "Error!",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }

        }

        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // Set current progress
            ProgressBar.Value = e.ProgressPercentage;
            ProgressBar.Content = e.UserState;
            // Send current progress to status view
            OnProgress?.Invoke(ProgressBar);
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!e.Cancelled)
            {

            }

            // Hide progressbar
            ProgressBar.Value = 0;
            ProgressBar.Content = "Ready";
            ProgressBar.Visibility = Visibility.Hidden;
            // Send current progress to status view
            OnProgress?.Invoke(ProgressBar);
            //Enable buttons
            Buttons.IsEnabled = true;
        }

    }

}
