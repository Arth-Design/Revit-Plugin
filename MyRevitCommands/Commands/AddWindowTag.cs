﻿using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace MyRevitCommands
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class AddWindowTag: IExternalCommand
    {
        private Document _doc;
        private UIDocument _uiDoc;

        /*
        public AddWindowTag(UIApplication uiapp)
        {
            _uiDoc = uiapp.ActiveUIDocument;
            _doc = _uiDoc.Document;
            _app = uiapp.Application;
        }
        */

        private XYZ MoveRight(XYZ point, double scaleFactor)
        {
            return new XYZ(point.X + scaleFactor, point.Y, point.Z);
        }

        private XYZ MoveDown(XYZ point, double scaleFactor)
        {
            return new XYZ(point.X, point.Y - scaleFactor, point.Z);
        }

        private XYZ MoveLeft(XYZ point, double scaleFactor)
        {
            return new XYZ(point.X - scaleFactor, point.Y, point.Z);
        }

        private XYZ MoveUp(XYZ point, double scaleFactor)
        {
            return new XYZ(point.X, point.Y + scaleFactor, point.Z);
        }

        private IEnumerable<XYZ> Shift(int end, XYZ point, double scaleFactor)
        {
            var moves = new List<System.Func<XYZ, double, XYZ>> { MoveRight, MoveDown, MoveLeft, MoveUp };
            var n = 1;
            var pos = point;
            var timesToMove = 1;

            yield return pos;

            while (true)
            {
                for (var i = 0; i < 2; i++)
                {
                    var move = moves[i % 4];
                    for (var j = 0; j < timesToMove; j++)
                    {
                        if (n >= end) yield break;
                        pos = move(pos, scaleFactor);
                        n++;
                        yield return pos;
                    }
                }
                timesToMove++;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document _doc = uidoc.Document;
            Selection sel = uidoc.Selection;

            // Prompt the user to make a selection
            var selection = uidoc.Selection;
            IList<Element> selectedElements = sel.PickElementsByRectangle();

            // Filter the windows based on their location
            var windowFiltered = new List<FamilyInstance>();
            foreach (var elem in selectedElements)
            {
                if (elem is FamilyInstance window && window.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows)
                {
                    windowFiltered.Add(window);
                }
            }

            var scaleFactor = 5.0;
            var windowFiltered1 = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .ToElements();

            var avoidLoc = new List<XYZ>();
            foreach (var d in windowFiltered)
            {
                var f = (d.Location as LocationPoint)?.Point;
                if (f != null) avoidLoc.Add(new XYZ(f.X, f.Y, f.Z));
            }

            // Get a list of available families for ducts
            FilteredElementCollector fec = new FilteredElementCollector(_doc);
            fec.OfClass(typeof(Family));
            List<Family> families = fec.Cast<Family>().ToList().Where(f => f.Name.Contains("Window Tag") || f.Name.Contains("Window_Tag")).ToList();
            List<string> familyNames = new List<string>();
            foreach (Family family in families)
            {
                familyNames.Add(family.Name);
            }

            // Create a form with radio buttons to select the family
            System.Windows.Forms.Form forma = new System.Windows.Forms.Form();
            forma.Text = "Select a family";
            forma.StartPosition = FormStartPosition.CenterScreen;
            forma.FormBorderStyle = FormBorderStyle.FixedDialog;
            forma.MinimizeBox = false;
            forma.MaximizeBox = false;
            forma.ShowInTaskbar = false;
            forma.AutoScroll = true;
            forma.ClientSize = new Size(500, 200);
            forma.BackColor = System.Drawing.Color.LightGray;

            GroupBox groupBox = new GroupBox();
            groupBox.Location = new System.Drawing.Point(10, 10);
            groupBox.Size = new Size(480, 140);
            forma.Controls.Add(groupBox);

            int y = 20;
            foreach (Family family in families)
            {
                RadioButton radioButton = new RadioButton();
                radioButton.Text = family.Name;
                radioButton.Location = new System.Drawing.Point(20, y);
                radioButton.AutoSize = true;
                radioButton.Tag = family;
                groupBox.Controls.Add(radioButton);
                y += 25;
            }

            System.Windows.Forms.Button okButton = new System.Windows.Forms.Button();
            okButton.Text = "OK";
            okButton.DialogResult = DialogResult.OK;
            okButton.Location = new System.Drawing.Point(180, 160);
            forma.Controls.Add(okButton);

            System.Windows.Forms.Button cancelButton = new System.Windows.Forms.Button();
            cancelButton.Text = "Cancel";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(260, 160);
            forma.Controls.Add(cancelButton);

            // Show the form and get the selected family
            Family selectedFamily = null;
            if (forma.ShowDialog() == DialogResult.OK)
            {
                foreach (System.Windows.Forms.Control control in groupBox.Controls)
                {
                    if (control is RadioButton && ((RadioButton)control).Checked)
                    {
                        selectedFamily = (Family)((RadioButton)control).Tag;
                        break;
                    }
                }
            }

            var tagFamilyName = "Window Tag"; // replace with your desired tag family name
            var tagFamily = selectedFamily;

            var tagSymbols = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_WindowTags)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(fs => fs.Family.Id == tagFamily.Id);

            if (tagSymbols == null)
            {
                TaskDialog.Show("Error", "No Tag Symbols Found");
                return Result.Failed;
            }

            foreach (var d in windowFiltered)
            {
                var lp = (d.Location as LocationPoint)?.Point;
                if (lp == null) continue;
                var levelPoint = new XYZ(lp.X, lp.Y, lp.Z);
                var R = new Reference(d);
                var tx = new Transaction(_doc);
                tx.Start("Tag Windows");
                var IT = IndependentTag.Create(_doc, uidoc.ActiveView.Id, R, true, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, levelPoint);
                IT.ChangeTypeId(tagSymbols.Id);
                tx.Commit();

                var tagBB = IT.get_BoundingBox((Autodesk.Revit.DB.View)_doc.GetElement(IT.OwnerViewId));
                var globalMax = tagBB.Max;
                var globalMin = tagBB.Min;
                var BBloc = new XYZ((globalMax.X), (globalMax.Y + globalMin.Y) / 2, globalMax.Z);
                var avoidPoints = Shift(50, BBloc, scaleFactor);

                foreach (var point in avoidPoints)
                {
                    var closestWindow = FindClosestWindow(point, avoidLoc);
                    if (closestWindow != null)
                    {
                        var distance = closestWindow.DistanceTo(point);
                        if (distance < scaleFactor)
                        {
                            var direction = (point - closestWindow).Normalize();
                            var newPoint = closestWindow + (direction * scaleFactor);
                            var tx1 = new Transaction(_doc);
                            tx1.Start("Modification of Tags");
                            IT.TagHeadPosition = newPoint;
                            IT.LeaderEndCondition = LeaderEndCondition.Free;
                            tx1.Commit();
                            break;
                        }
                    }
                }
            }
            return Result.Succeeded;
        }

        private XYZ FindClosestWindow(XYZ point, List<XYZ> avoidLoc)
        {
            XYZ closestWindow = null;
            var closestDistance = double.MaxValue;
            foreach (var window in avoidLoc)
            {
                var distance = window.DistanceTo(point);
                if (distance < closestDistance)
                {
                    closestWindow = window;
                    closestDistance = distance;
                }
            }
            return closestWindow;
        }
    }
}