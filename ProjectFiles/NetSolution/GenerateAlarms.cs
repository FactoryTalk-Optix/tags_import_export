#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.CODESYS;
using FTOptix.S7TiaProfinet;
using FTOptix.NetLogic;
using FTOptix.NativeUI;
using FTOptix.UI;
using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.Alarm;
using System.Linq;
#endregion

public class GenerateAlarms : BaseNetLogic
{
    // TODO: handle the "update" case where the alarm to be generated already exists: now it skipts the alarm if altready exists
    // TODO: handle more cases in "FetchNode" method
    // TODO: handle more cases in "GenerateDigitalAlarm" method

    private Folder alarmsFolder;

    [ExportMethod]
    public void ClearAlarmsFolder()
    {
        alarmsFolder = Project.Current.Get<Folder>("Alarms");
        alarmsFolder.Children.Clear();
    }

    [ExportMethod]
    public void GenerateDigitalAlarms()
    {
        var startingNode = InformationModel.Get(LogicObject.GetVariable("startingNode").Value);
        alarmsFolder = Project.Current.Get<Folder>("Alarms");

        FetchNode(startingNode);
    }

    private void FetchNode(IUANode startingNode)
    {
        switch (startingNode)
        {
            case TagStructure _:
            case Folder _:
                foreach (var item in startingNode.Children)
                {
                    FetchNode(item);
                }
                break;
            case FTOptix.CommunicationDriver.Tag:
            case UAVariable:
                GenerateDigitalAlarm((UAVariable)startingNode);
                break;
            default:
                break;
        }
    }

    private void GenerateDigitalAlarm(UAVariable variable)
    {
        try
        {
            var tagDataType = variable.DataType;
            var numberOfAlarmsToGenerate = 1;
            var variableIsArray = variable.ArrayDimensions.Length == 1;
            var isBoolVariable = false;
            var isArrayVariable = variable.ArrayDimensions.Length > 0;

            if (OpcUa.DataTypes.Boolean == tagDataType) isBoolVariable = true;
            if (OpcUa.DataTypes.Int16 == tagDataType) numberOfAlarmsToGenerate = sizeof(Int16) * 8;
            if (OpcUa.DataTypes.Int32 == tagDataType) numberOfAlarmsToGenerate = sizeof(Int32) * 8;
            if (OpcUa.DataTypes.UInt16 == tagDataType) numberOfAlarmsToGenerate = sizeof(UInt16) * 8;
            if (OpcUa.DataTypes.UInt32 == tagDataType) numberOfAlarmsToGenerate = sizeof(UInt32) * 8;

            var alarmsFolderTemp = GetVariableAlmsFolder(variable, isArrayVariable, isBoolVariable);
            DigitalAlarm alm = null;

            if (variableIsArray)
            {
                for (uint i = 0; i < variable.ArrayDimensions[0]; i++)
                {
                    if (isBoolVariable)
                    {
                        alm = GenerateDAl(variable, i);
                        alm.InputValueVariable.SetDynamicLink(variable, i, DynamicLinkMode.ReadWrite);
                        alm.Message = GenerateAlMessage(variable, i);
                        if (!NodeAlreadyExists(alarmsFolderTemp, alm)) alarmsFolderTemp.Add(alm);
                    }
                    else
                    {
                        for (uint j = 0; j < numberOfAlarmsToGenerate; j++)
                        {
                            alm = GenerateDAl(variable, i, j);
                            alm.InputValueVariable.SetDynamicLink(variable, i, DynamicLinkMode.ReadWrite);
                            AddBitDynamicLink(alm, j);
                            alm.Message = GenerateAlMessage(variable, i, j);
                            if (!NodeAlreadyExists(alarmsFolderTemp, alm)) alarmsFolderTemp.Add(alm);
                        }
                    }
                }
            }
            else
            {
                for (uint i = 0; i < numberOfAlarmsToGenerate; i++)
                {
                    if (isBoolVariable)
                    {
                        alm = GenerateDAl(variable);
                        alm.InputValueVariable.SetDynamicLink(variable, DynamicLinkMode.ReadWrite);
                    }
                    else
                    {
                        alm = GenerateDAl(variable, i);
                        alm.InputValueVariable.SetDynamicLink(variable, DynamicLinkMode.ReadWrite);
                        AddBitDynamicLink(alm, i);
                    }
                    alm.Message = GenerateAlMessage(variable, i);
                    if (!NodeAlreadyExists(alarmsFolderTemp, alm)) alarmsFolderTemp.Add(alm);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex.Message);
        }
    }

    private DigitalAlarm GenerateDAl(UAVariable variable, uint? i = null, uint? j = null)
    {
        var alarmName = $"DAlm_{variable.Owner.BrowseName}_{variable.BrowseName}";
        alarmName = i != null ? j != null ? $"{alarmName}_{i}_{j}" : $"{alarmName}_{i}" : alarmName;
        return InformationModel.Make<DigitalAlarm>(alarmName);
    }

    private string GenerateAlMessage(UAVariable variable, uint i, uint? j = null)
    {
        if (j != null)
        {
            return $"DAlm_{variable.Owner.BrowseName}_{variable.BrowseName}_{i}_{j}";
        }
        else
        {
            return $"DAlm_{variable.Owner.BrowseName}_{variable.BrowseName}_{i}";
        }
    }

    private IUAVariable AddBitDynamicLink(DigitalAlarm alm, uint i)
    {
        var almDl = alm.InputValueVariable.GetVariable("DynamicLink");
        almDl.Value = almDl.Value + "." + i;
        return almDl;
    }

    private bool NodeAlreadyExists(IUANode parent, IUANode child)
    {
        var res = parent.Children.Any(c => c.BrowseName == child.BrowseName);
        if (res) Log.Warning(child.BrowseName + " already exists into " + parent.BrowseName);
        return res;
    }

    private Folder GetVariableAlmsFolder(UAVariable variable, bool isArrayVariable, bool isBoolVariable)
    {
        if (!isArrayVariable && isBoolVariable) return alarmsFolder;
        var variableAlmsFolder = InformationModel.Make<Folder>($"{variable.Owner.BrowseName}_{variable.BrowseName}_alarms");
        if (NodeAlreadyExists(alarmsFolder, variableAlmsFolder)) variableAlmsFolder = alarmsFolder.Children.Get<Folder>(variableAlmsFolder.BrowseName);
        alarmsFolder.Add(variableAlmsFolder);
        return variableAlmsFolder;
    }
}
