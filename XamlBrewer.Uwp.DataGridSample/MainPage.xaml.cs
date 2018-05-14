using Microsoft.Toolkit.Uwp.UI.Controls;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace XamlBrewer.Uwp.DataGridSample
{
    public sealed partial class MainPage : Page
    {
        private string connectionString = "YourConnectionStringHere";
        private DataTable dataTable;

        public MainPage()
        {
            this.InitializeComponent();

            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            if (titleBar != null)
            {
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = Colors.DarkSlateGray;
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                }

                var dialog = new MessageDialog("Connected")
                {
                    Title = "OK"
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new MessageDialog(ex.Message)
                {
                    Title = "Error"
                };

                await dialog.ShowAsync();
            }
        }

        private void Query1Button_Click(object sender, RoutedEventArgs e)
        {
            var query = @"
                    SELECT Replace(Lower(type_desc), '_', ' ') AS [Object Type], COUNT(*) AS [Number Of Entities]
                    FROM sys.objects
                    WHERE type_desc NOT IN ('SYSTEM_TABLE', 'INTERNAL_TABLE')
                    GROUP BY type_desc";

            ExecuteQuery(query);
        }

        private void Query2Button_Click(object sender, RoutedEventArgs e)
        {
            var query = @"
                WITH cte AS
                (
                SELECT DISTINCT fk.constraint_object_id, 0 AS ccid, CONVERT(NVARCHAR(MAX), '') AS cs
                  FROM sys.foreign_key_columns fk
                  LEFT OUTER JOIN sys.index_columns ic
                     ON ic.object_id = fk.parent_object_id  /* same table */
                        AND ic.column_id = fk.parent_column_id  /* same column */
                        AND ic.index_column_id = fk.constraint_column_id /* same column position */
                WHERE ic.object_id IS NULL
                
                UNION ALL
                
                SELECT cte.constraint_object_id, fk.constraint_column_id, cte.cs + ', ' + fc.name
                FROM cte
                  JOIN sys.foreign_key_columns fk ON cte.constraint_object_id = fk.constraint_object_id AND cte.ccid + 1 = fk.constraint_column_id
                  JOIN sys.columns fc ON fk.parent_object_id = fc.object_id AND fk.parent_column_id = fc.column_id
                )
                SELECT MAX(fks.name) AS [Schema], MAX(fkt.name) AS [Table], SUBSTRING(MAX(cs), 2, 9999999) AS [Columns], MAX(fko.name) AS [Foreign Key], MAX(fkrs.name) AS [Referenced Schema], MAX(fkr.name) AS [Referenced Table]
                  FROM cte
                  JOIN sys.foreign_key_columns fk ON cte.constraint_object_id = fk.constraint_object_id AND cte.ccid = fk.constraint_column_id
                  JOIN sys.objects fkt ON fk.parent_object_id = fkt.object_id
                  JOIN sys.schemas fks ON fks.schema_id = fkt.schema_id
                  JOIN sys.objects fko ON fk.constraint_object_id = fko.object_id
                  JOIN sys.objects fkr ON fk.referenced_object_id = fkr.object_id
                  JOIN sys.schemas fkrs ON fkr.schema_id = fkrs.schema_id
                GROUP BY cte.constraint_object_id";

            ExecuteQuery(query);
        }

        private void Query3Button_Click(object sender, RoutedEventArgs e)
        {
            var query = @"
                SELECT TOP 15
                    SUM(query_stats.total_worker_time) / SUM(query_stats.execution_count) AS 'Avg CPU Time',
                    MIN(query_stats.statement_text) AS 'SQL Statement',
                    MIN(query_stats.statement_text) AS 'Full SQL Statement'
                FROM
                    (SELECT QS.*,
                    SUBSTRING(ST.text, (QS.statement_start_offset / 2) + 1,
                    ((CASE statement_end_offset
                        WHEN - 1 THEN DATALENGTH(ST.text)
                        ELSE QS.statement_end_offset END
                            - QS.statement_start_offset) / 2) + 1) AS statement_text
                     FROM sys.dm_exec_query_stats AS QS
                     CROSS APPLY sys.dm_exec_sql_text(QS.sql_handle) as ST) as query_stats
                GROUP BY query_stats.query_hash
                ORDER BY 1 DESC;";

            ExecuteQuery(query);
        }

        private async void ExecuteQuery(string query)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = query;
                dataTable = new DataTable();

                using (var dataAdapter = new SqlDataAdapter(command))
                {
                    dataAdapter.Fill(dataTable);
                }
            }

            BindTable(dataTable, ResultsGrid);

            if (dataTable.Rows.Count == 0)
            {
                ResultsGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                ResultsGrid.Visibility = Visibility.Visible;
            }
        }

        private void BindTable(DataTable table, DataGrid grid)
        {
            // Generate columns with index binding

            grid.Columns.Clear();
            grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;

            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (table.Columns[i].ColumnName == "Full SQL Statement")
                {
                    // Treat 'Full SQL Statement' column differently.
                    grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
                }
                else
                {
                    grid.Columns.Add(new DataGridTextColumn()
                    {
                        Header = table.Columns[i].ColumnName,
                        Binding = new Binding { Path = new PropertyPath("[" + i.ToString() + "]") }
                    });
                }
            }

            // Post-process 'SQL Statement' column.
            if (table.Columns.Contains("SQL Statement"))
            {
                var column = table.Columns["SQL Statement"];

                foreach (DataRow row in table.Rows)
                {
                    string sqlStatement = ((row[column] as string) ?? string.Empty).Trim();
                    row[column] = string.Join(' ', sqlStatement.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries)).Substring(0, 80) + "...";
                }

                table.AcceptChanges();
            }

            RefreshContents(table, grid);
        }

        private void RefreshContents(DataTable table, DataGrid grid)
        {
            // Create collection
            var collection = new ObservableCollection<object>();
            foreach (DataRow row in table.Rows)
            {
                collection.Add(row.ItemArray);
            }

            grid.ItemsSource = collection;
        }

        private void ResultsGrid_Sorting(object sender, DataGridColumnEventArgs e)
        {
            var currentSortDirection = e.Column.SortDirection;

            foreach (var column in ResultsGrid.Columns)
            {
                column.SortDirection = null;
            }

            var sortOrder = "ASC";

            if ((currentSortDirection == null || currentSortDirection == DataGridSortDirection.Descending))
            {
                e.Column.SortDirection = DataGridSortDirection.Ascending;
            }
            else
            {
                sortOrder = "DESC";
                e.Column.SortDirection = DataGridSortDirection.Descending;
            }

            var dataView = dataTable.DefaultView;
            dataView.Sort = e.Column.Header + " " + sortOrder;
            dataTable = dataView.ToTable();

            RefreshContents(dataTable, ResultsGrid);
        }
    }
}
