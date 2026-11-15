// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// Places the Routing items in columns, left to right (Routers, Routes, Targets), with each item centered on its children
    /// </summary>
    public static class RoutingLayout
    {
        public const double GridSize = 15;
        public const double ColumnWidth = 330;
        public const double RowHeight = 105;


        /// <summary>
        /// The position of every item (Item ID => X, Y). Routers are placed first (by Name), then the items that aren't connected
        /// </summary>
        public static Dictionary<string, (double X, double Y)> Arrange(RoutingModel model)
        {
            var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
            if (model == null) return positions;

            var row = 0;

            var routers = model.Routers.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Id, StringComparer.Ordinal);
            foreach (var router in routers)
            {
                Place(router, 0, ref row, positions);
            }

            // Leave a gap above the items that aren't connected
            if (row > 0 && model.Unconnected.Any()) row++;

            foreach (var item in model.Unconnected.OrderBy(o => o.Kind).ThenBy(o => o.Id, StringComparer.Ordinal))
            {
                Place(item, (int)item.Kind, ref row, positions);
            }

            return positions;
        }

        /// <summary>
        /// Where to put a new item connected to the Parent: in the next column, below the Parent's other children
        /// </summary>
        public static (double X, double Y) GetChildPosition((double X, double Y) parent, IEnumerable<(double X, double Y)> siblings)
        {
            var below = siblings?.Select(o => o.Y + RowHeight).DefaultIfEmpty(parent.Y).Max() ?? parent.Y;
            return (Snap(parent.X + ColumnWidth), Snap(Math.Max(parent.Y, below)));
        }

        public static double Snap(double value) => Math.Round(value / GridSize) * GridSize;


        // Returns the Y of the item
        private static double Place(RoutingItem item, int column, ref int row, Dictionary<string, (double X, double Y)> positions)
        {
            double y;
            if (item.Children.Count == 0)
            {
                y = row * RowHeight;
                row++;
            }
            else
            {
                var first = double.NaN;
                var last = 0.0;
                foreach (var child in item.Children)
                {
                    var childY = Place(child, column + 1, ref row, positions);
                    if (double.IsNaN(first)) first = childY;
                    last = childY;
                }
                y = (first + last) / 2;
            }

            positions[item.Id] = (Snap(column * ColumnWidth), Snap(y));
            return y;
        }
    }
}
