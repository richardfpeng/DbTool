﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DbTool.Core;
using DbTool.Core.Entity;
using DbTool.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.Win32;
using NPOI.SS.UserModel;
using WeihanLi.Common;
using WeihanLi.Extensions;
using WeihanLi.Npoi;

namespace DbTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly IStringLocalizer<MainWindow> _localizer;
        private readonly DbProviderFactory _dbProviderFactory;
        private readonly SettingsViewModel _settings;

        private readonly IModelCodeGenerator _modelCodeGenerator;
        private readonly IModelNameConverter _modelNameConverter;

        public MainWindow(
            IStringLocalizer<MainWindow> localizer,
            DbProviderFactory dbProviderFactory,
            SettingsViewModel settings,
            IModelCodeGenerator modelCodeGenerator,
            IModelNameConverter modelNameConverter)
        {
            InitializeComponent();

            _localizer = localizer;
            _settings = settings;
            _dbProviderFactory = dbProviderFactory;
            _modelCodeGenerator = modelCodeGenerator;
            _modelNameConverter = modelNameConverter;

            InitDataBinding();

            TxtNamespace.Text = "SystemManagement.Entity";

        }

        private void InitDataBinding()
        {
            DataContext = _settings;

            DbFirst_DbType.ItemsSource = _dbProviderFactory.SupportedDbTypes;
            DbFirst_DbType.SelectedItem = _settings.DefaultDbType;

            DefaultDbType.ItemsSource = _dbProviderFactory.SupportedDbTypes;
            DefaultDbType.SelectedItem = _settings.DefaultDbType;

            var supportedCultures = _settings.SupportedCultures
                .Select(c => new CultureInfo(c))
                .ToArray();
            DefaultCulture.ItemsSource = supportedCultures;
            DefaultCulture.SelectedItem = supportedCultures.FirstOrDefault(c => c.Name == _settings.DefaultCulture);

            CbGenPrivateFields.IsChecked = _settings.GeneratePrivateField;
            CbGenDataAnnotation.IsChecked = _settings.GenerateDataAnnotation;
            CbApplyNameConverter.IsChecked = _settings.ApplyNameConverter;
            CodeGenDbDescCheckBox.IsChecked = _settings.GenerateDbDescription;
            ModelFirstGenDesc.IsChecked = _settings.GenerateDbDescription;

            var exporters = DependencyResolver.Current.ResolveServices<IDbDocExporter>();
            foreach (var exporter in exporters)
            {
                var exportButton = new Button()
                {
                    Content = $"{_localizer["Export"]}{exporter.ExportType}",
                    Tag = exporter,
                    MaxWidth = 160,
                    Margin = new Thickness(4)
                };
                exportButton.Click += ExportButton_Click;
                DbDocExportersPanel.Children.Add(exportButton);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (CheckedTables.SelectedItems.Count == 0)
            {
                MessageBox.Show(_localizer["ChooseTables"], _localizer["Tip"]);
                return;
            }

            if (sender is Button btnExport && btnExport.Tag is IDbDocExporter exporter)
            {
                var tables = new List<TableEntity>();
                foreach (var item in CheckedTables.SelectedItems)
                {
                    if (item is TableEntity table)
                    {
                        tables.Add(table);
                    }
                }
                if (tables.Count == 0) return;
                //
                var dir = ChooseFolder();
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                try
                {
                    var exportBytes = exporter.Export(tables.ToArray(), _dbHelper.DbType);
                    if (null != exportBytes && exportBytes.Length > 0)
                    {
                        var fileName = tables.Count > 1
                            ? _dbHelper.DatabaseName
                            :
                                (_settings.ApplyNameConverter
                                    ? _modelNameConverter.ConvertTableToModel(tables[0].TableName)
                                    : tables[0].TableName)
                            ;
                        fileName = $"{fileName}.{exporter.FileExtension.TrimStart('.')}";
                        var path = Path.Combine(dir, fileName);
                        File.WriteAllBytes(path, exportBytes);
                        // open dir
                        Process.Start("Explorer.exe", dir);
                    }
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.ToString(), "Export Error");
                }
            }
        }

        private void BtnSaveSettings_OnClick(object sender, RoutedEventArgs e)
        {
            if (null != DefaultDbType.SelectedItem && _settings.DefaultDbType != DefaultDbType.SelectedItem.ToString())
            {
                _settings.DefaultDbType = DefaultDbType.SelectedItem?.ToString();
            }
            if (DefaultCulture.SelectedItem is CultureInfo culture && culture.Name != _settings.DefaultCulture)
            {
                _settings.DefaultCulture = culture.Name;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            if (_settings.DefaultConnectionString != TxtDefaultConnStr.Text.Trim())
            {
                _settings.DefaultConnectionString = TxtDefaultConnStr.Text.Trim();
            }
            MessageBox.Show(_localizer["Success"], _localizer["Tip"]);
        }

        private void BtnChooseModel_OnClick(object sender, RoutedEventArgs e)
        {
            var ofg = new OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = true,
                Filter = "C# File(*.cs)|*.cs"
            };
            if (ofg.ShowDialog() == true)
            {
                if (ofg.FileNames.Any(f => !f.EndsWith(".cs")))
                {
                    MessageBox.Show(_localizer["UnsupportedFileType", ofg.FileNames.First(f => !f.EndsWith(".cs"))]);
                    return;
                }

                try
                {
                    var dbProvider = _dbProviderFactory.GetDbProvider(_settings.DefaultDbType);
                    var tables = dbProvider.GetTableEntityFromSourceCode(ofg.FileNames);
                    if (tables == null)
                    {
                        MessageBox.Show(_localizer["NoModelFound"]);
                    }
                    else
                    {
                        TxtCodeGenSql.Clear();
                        foreach (var table in tables)
                        {
                            var tableSql = dbProvider.GenerateSqlStatement(table, CodeGenDbDescCheckBox.IsChecked == true);
                            TxtCodeGenSql.AppendText(tableSql);
                            TxtCodeGenSql.AppendText(Environment.NewLine);
                        }

                        CodeGenTableTreeView.ItemsSource = tables;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }
        }

        private void BtnGenerateSql_OnClick(object sender, RoutedEventArgs e)
        {
            if (ModelDataGrid.Items.Count > 0)
            {
                if (string.IsNullOrEmpty(TxtModelFirstTableName.Text))
                {
                    return;
                }

                var table = new TableEntity()
                {
                    TableName = TxtModelFirstTableName.Text,
                    TableDescription = TxtModelFirstTableDesc.Text,
                };
                foreach (var item in ModelDataGrid.Items)
                {
                    if (item is ColumnEntity column && !string.IsNullOrEmpty(column.ColumnName))
                    {
                        table.Columns.Add(column);
                    }
                }
                var dbProvider = _dbProviderFactory.GetDbProvider(_settings.DefaultDbType);
                var sql = dbProvider.GenerateSqlStatement(table, ModelFirstGenDesc.IsChecked == true);
                TxtModelFirstGeneratedSql.Text = sql;
                Clipboard.SetText(sql);
                MessageBox.Show(_localizer["SqlCopiedToClipboard"], _localizer["Tip"]);
            }
        }

        private void BtnImportModelExcel_OnClick(object sender, RoutedEventArgs e)
        {
            var ofg = new OpenFileDialog
            {
                Multiselect = false,
                CheckFileExists = true,
                Filter = "Excel file(*.xls)|*.xls|Excel file(*.xlsx)|*.xlsx"
            };
            if (ofg.ShowDialog() == true)
            {
                try
                {
                    var workbook = ExcelHelper.LoadExcel(ofg.FileName);
                    if (0 == workbook.NumberOfSheets)
                    {
                        return;
                    }
                    var tableCount = workbook.NumberOfSheets;
                    var sql = string.Empty;
                    var dbProvider = _dbProviderFactory.GetDbProvider(_settings.DefaultDbType);
                    if (tableCount == 1)
                    {
                        var sheet = workbook.GetSheetAt(0);
                        var table = ExactTableFromExcel(sheet, dbProvider);

                        TxtModelFirstTableName.Text = table.TableName;
                        TxtModelFirstTableDesc.Text = table.TableDescription;
                        ModelDataGrid.ItemsSource = table.Columns;

                        sql = dbProvider.GenerateSqlStatement(table, ModelFirstGenDesc.IsChecked == true);
                    }
                    else
                    {
                        var sbSqlText = new StringBuilder();
                        for (var i = 0; i < tableCount; i++)
                        {
                            var sheet = workbook.GetSheetAt(i);
                            var table = ExactTableFromExcel(sheet, dbProvider);
                            if (i > 0)
                            {
                                sbSqlText.AppendLine();
                            }
                            else
                            {
                                TxtModelFirstTableName.Text = table.TableName;
                                TxtModelFirstTableDesc.Text = table.TableDescription;
                                ModelDataGrid.ItemsSource = table.Columns;
                            }
                            sbSqlText.AppendLine(dbProvider.GenerateSqlStatement(table, ModelFirstGenDesc.IsChecked == true));
                        }
                        sql = sbSqlText.ToString();
                    }
                    TxtModelFirstGeneratedSql.Text = sql;
                    Clipboard.SetText(sql);
                    MessageBox.Show(_localizer["SqlCopiedToClipboard"], _localizer["Tip"]);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "Error");
                }
            }
        }

        private TableEntity ExactTableFromExcel(ISheet sheet, IDbProvider dbProvider)
        {
            if (sheet == null)
                return null;

            var table = new TableEntity
            {
                TableName = sheet.SheetName
            };

            foreach (var row in sheet.GetRowCollection())
            {
                if (null == row)
                {
                    continue;
                }
                if (row.RowNum == 0)
                {
                    table.TableDescription = row.Cells[0].StringCellValue;
                    continue;
                }
                if (row.RowNum > 1)
                {
                    var column = new ColumnEntity
                    {
                        ColumnName = row.GetCell(0)?.StringCellValue
                    };
                    if (string.IsNullOrWhiteSpace(column.ColumnName))
                    {
                        continue;
                    }
                    column.ColumnDescription = row.GetCell(1).StringCellValue;
                    column.IsPrimaryKey = row.GetCell(2).StringCellValue.Equals("Y");
                    column.IsNullable = row.GetCell(3).StringCellValue.Equals("Y");
                    column.DataType = row.GetCell(4).StringCellValue;

                    column.Size = string.IsNullOrEmpty(row.GetCell(5).ToString()) ? dbProvider.GetDefaultSizeForDbType(column.DataType) : Convert.ToUInt32(row.GetCell(5).ToString());

                    if (!string.IsNullOrWhiteSpace(row.GetCell(6)?.ToString()))
                    {
                        column.DefaultValue = row.GetCell(6).ToString();
                    }
                    table.Columns.Add(column);
                }
            }

            return table;
        }

        private void DownloadExcelTemplateLink_OnClick(object sender, RoutedEventArgs e)
        {
            // https://stackoverflow.com/questions/59716856/net-core-3-1-process-startwww-website-com-not-working-in-wpf
            Process.Start(new ProcessStartInfo(_settings.ExcelTemplateDownloadLink)
            {
                UseShellExecute = true
            });
        }

        private DbHelper _dbHelper;

        private async void BtnConnectDb_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtConnectionString.Text))
            {
                MessageBox.Show(_localizer["ConnectionStringCannotBeEmpty"]);
                return;
            }
            try
            {
                _dbHelper?.Dispose();

                var connStr = TxtConnectionString.Text;
                var currentDbProvider = _dbProviderFactory.GetDbProvider(DbFirst_DbType.SelectedItem.ToString());
                _dbHelper = new DbHelper(connStr, currentDbProvider);

                var tables = await _dbHelper.GetTablesInfoAsync();
                CheckedTables.Dispatcher.Invoke(() =>
                {
                    CheckedTables.ItemsSource = tables
                        .OrderBy(x => x.TableName)
                        .ToArray();
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), "Error");
            }
        }
        
        private void GenServiceCode_OnClick(object sender, RoutedEventArgs e)
        {
            //Controller
            //IService
            //Service
            //IRepository
            //Repository

        }


        private void BtnExportModel_OnClick(object sender, RoutedEventArgs e)
        {
            if (CheckedTables.SelectedItems.Count == 0)
            {
                MessageBox.Show(_localizer["ChooseTables"]);
                return;
            }
            var options = new ModelCodeGenerateOptions()
            {
                Namespace = TxtNamespace.Text.GetValueOrDefault("SystemManagement.Entity"),
                Prefix = TxtPrefix.Text,
                Suffix = TxtSuffix.Text,
                GenerateDataAnnotation = CbGenDataAnnotation.IsChecked == true,
                GeneratePrivateFields = CbGenPrivateFields.IsChecked == true,
                ApplyNameConverter = CbApplyNameConverter.IsChecked == true,
            };
            var dir = ChooseFolder();
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            foreach (var item in CheckedTables.SelectedItems)
            {
                if (item is TableEntity table)
                {
                    var modelCode = _modelCodeGenerator.GenerateModelCode(table, options, _dbHelper.DbType);
                    var path = Path.Combine(dir, $"{(_settings.ApplyNameConverter ? _modelNameConverter.ConvertTableToModel(table.TableName) : table.TableName)}.cs");
                    File.WriteAllText(path, modelCode, Encoding.UTF8);


                    var dialogCode = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateDialogCode(table, options, _dbHelper.DbType);
                    path = Path.Combine(dir, $"{(_settings.ApplyNameConverter ? _modelNameConverter.ConvertTableToModel(table.TableName) : table.TableName)}.vue");
                    File.WriteAllText(path, dialogCode, Encoding.UTF8);


                    string modelName = _settings.ApplyNameConverter ? _modelNameConverter.ConvertTableToModel(table.TableName) : table.TableName;
                    string IFName = $"I{modelName}Repository";
                    
                    var code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateIRepositoryCode(IFName, modelName);
                    path = Path.Combine(dir, $"{IFName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);

                    string classRepositoryName = $"{modelName}Repository";
                    code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateRepositoryCode(IFName, classRepositoryName, modelName);
                    path = Path.Combine(dir, $"{classRepositoryName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);


                    string IServiceName = $"I{modelName}Service";
                    string DtoName = $"{modelName}Dto";

                    code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateDTOCode(table, options, modelName, DtoName, _dbHelper.DbType);
                    path = Path.Combine(dir, $"{DtoName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);


                    code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateIServiceCode(IServiceName, DtoName);
                    path = Path.Combine(dir, $"{IServiceName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);


                    string ServiceName = $"{modelName}Service";
                    code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateServiceCode(ServiceName, IServiceName, IFName, DtoName, modelName);
                    path = Path.Combine(dir, $"{ServiceName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);

                    //Controller
                    string controllerName = $"{modelName}Controller";
                    code = ((DefaultModelCodeGenerator)_modelCodeGenerator).GenerateControllerCode(controllerName, ServiceName, IServiceName, IFName, DtoName, modelName);
                    path = Path.Combine(dir, $"{controllerName}.cs");
                    File.WriteAllText(path, code, Encoding.UTF8);

                  
                    //Place files!
                    //Bool ==> tinybit

                }
            }
            // open dir
            Process.Start("Explorer.exe", dir);
        }

        private string ChooseFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = _localizer["ChooseDirTip"],
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return dialog.SelectedPath;
            }
            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            _dbHelper?.Dispose();
            base.OnClosed(e);
        }

        private async void CheckTableToggled(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is TableEntity table)
            {
                if (checkBox.IsChecked == true)
                {
                    if (CheckedTables.SelectedItems.Contains(table) == false)
                    {
                        CheckedTables.SelectedItems.Add(table);
                    }

                    if (table.TableName != CurrentCheckedTableName.Text)
                    {
                        CurrentCheckedTableName.Text = table.TableName;
                        if (table.Columns.Count == 0)
                        {
                            table.Columns = await _dbHelper.GetColumnsInfoAsync(table.TableName);
                            ColumnListView.Dispatcher.Invoke(() =>
                            {
                                ColumnListView.ItemsSource = table.Columns;
                            });
                        }
                        else
                        {
                            ColumnListView.ItemsSource = table.Columns;
                        }
                    }
                }
                else
                {
                    if (CheckedTables.SelectedItems.Contains(table))
                    {
                        CheckedTables.SelectedItems.Remove(table);
                    }
                }
            }
        }
    }
}
