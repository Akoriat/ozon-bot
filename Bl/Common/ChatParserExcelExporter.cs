using ClosedXML.Excel;

namespace Bl.Common
{
    public static class ChatParserExcelExporter
    {
        public static void SaveToExcel(List<List<string>> data, string filePath)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Chats");
                int row = 1;
                foreach (var r in data)
                {
                    int col = 1;
                    foreach (var cell in r)
                    {
                        ws.Cell(row, col++).Value = cell;
                    }
                    row++;
                }
                workbook.SaveAs(filePath);
            }
        }

        public static List<List<string>> LoadFromExcel(string filePath)
        {
            var data = new List<List<string>>();
            if (!File.Exists(filePath))
                return data;
            using (var workbook = new XLWorkbook(filePath))
            {
                var ws = workbook.Worksheets.First();
                foreach (var row in ws.RowsUsed())
                {
                    var list = new List<string>();
                    foreach (var cell in row.CellsUsed())
                    {
                        list.Add(cell.GetValue<string>());
                    }
                    data.Add(list);
                }
            }
            return data;
        }
    }
}
