#region Using directives
using System;
using System.Text;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FTOptix.CommunicationDriver;
using FTOptix.S7TCP;
using FTOptix.S7TiaProfinet;
using FTOptix.Alarm;
#endregion

public class ImportExportTags : BaseNetLogic
{
    private string csvFilename;
    private string tagsCsvUri;
    private IUANode startingNode;
    private const string csvSeparator = ";";
    private const string CSV_FILENAME = "tags.csv";
    private const string arrayLengthSeparator = ",";
    private int tagsCreated = 0;
    private int tagsUpdated = 0;
    private int tagStructuresCreated = 0;
    private List<string> customTagsPropertiesNames = new List<string> { "Type", "BrowseName", "BrowsePath", "NodeDataType", "ArrayLength" };
    private List<string> tagsPropertiesNames;

    // TODO: gestire anche tipo Folder 
    // TODO: OPC-UA Client?

    [ExportMethod]
    public void ExportToCsv()
    {
        RetrieveParameters();
        WriteTagsToCsv(startingNode);
    }

    [ExportMethod]
    public void ImportOrUpdateFromCsv()
    {
        RetrieveParameters();
        try
        {
            using (StreamReader reader = new StreamReader(tagsCsvUri))
            {
                string line = reader.ReadLine();
                string[] header = line.Split(csvSeparator); ;
                if (line == null) return;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] values = line.Split(csvSeparator);
                    CreateOrUpdateTagFromCsvLine(values, header);
                }
            }
            Log.Info("Tags updated: " + tagsUpdated + " Tags created: " + tagsCreated + " TagStructure created: " + tagStructuresCreated);
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void CreateOrUpdateTagFromCsvLine(string[] values, string[] header)
    {
        try
        {
            var tagTypeString = values[GetElementIndex(header, customTagsPropertiesNames[0])];
            if (tagTypeString == typeof(FTOptix.CommunicationDriver.TagStructure).FullName)
            {
                var arrayDim = GetArrayLengthString(values, header);
                if (arrayDim != string.Empty)
                {
                    var tagStructureArray = InformationModel.MakeVariable<FTOptix.CommunicationDriver.TagStructure>(
                        GetBrowseName(values, header),
                        OpcUa.DataTypes.Structure,
                        new uint[] { uint.Parse(arrayDim) });
                    GenerateTagStructure(values, header, tagStructureArray);
                }
                else
                {
                    var tagStructure = InformationModel.Make<FTOptix.CommunicationDriver.TagStructure>(GetBrowseName(values, header));
                    GenerateTagStructure(values, header, tagStructure);
                }
            }
            else
            {
                GenerateTag(values, header, tagTypeString);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void GenerateTagStructure(string[] values, string[] header, UAVariable tStructure)
    {
        var tagBrowseName = GetBrowseName(values, header);
        var tagBrowsePath = GetBrowsePath(values, header);
        var owner = GetOwnerNode(startingNode, tagBrowsePath);
        var alreadyExistingNode = NodeAlreadyExists(owner, tStructure) != null;

        if (!alreadyExistingNode)
        {
            owner.Add(tStructure);
            tagStructuresCreated++;
        };
    }

    private void GenerateTag(string[] values, string[] header, string tagTypeString)
    {
        try
        {
            FTOptix.CommunicationDriver.Tag tag;
            var tagBrowseName = GetBrowseName(values, header);
            var tagBrowsePath = GetBrowsePath(values, header);
            var tagDataTypeString = GetDataTypeString(values, header);
            var tagArrayLengthString = GetArrayLengthString(values, header);

            if (tagTypeString == typeof(FTOptix.S7TCP.Tag).FullName) tag = InformationModel.Make<FTOptix.S7TCP.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.CODESYS.Tag).FullName) tag = InformationModel.Make<FTOptix.CODESYS.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.Modbus.Tag).FullName) tag = InformationModel.Make<FTOptix.Modbus.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.RAEtherNetIP.Tag).FullName) tag = InformationModel.Make<FTOptix.RAEtherNetIP.Tag>(tagBrowseName);
            else throw new NotImplementedException();

            tag.DataType = GetOpcUaDataType(tagDataTypeString);

            if (tagArrayLengthString != string.Empty) SetTagArrayDimensions(tag, tagArrayLengthString);

            PropertyInfo[] tagProperties = GetTypePropertieInfos(tag.GetType());
            foreach (var p in tagProperties)
            {
                if (!p.CanWrite) continue;
                var gropertyCsvIndex = GetElementIndex(header, p.Name);
                if (gropertyCsvIndex == -1) continue;
                var v = values[gropertyCsvIndex];
                if (string.IsNullOrEmpty(v)) continue;
                if (p.PropertyType.IsEnum)
                {
                    if (p.PropertyType.Name == "ValueRank") continue;
                    var enumValue = Enum.Parse(p.PropertyType, v);
                    SetPropertyValue(tag, p, enumValue);
                }
                else
                {
                    switch (Type.GetTypeCode(p.PropertyType))
                    {
                        case TypeCode.Int16:
                            SetPropertyValue(tag, p.Name, Int16.Parse(v));
                            break;
                        case TypeCode.Int32:
                            SetPropertyValue(tag, p.Name, Int32.Parse(v));
                            break;
                        case TypeCode.UInt16:
                            SetPropertyValue(tag, p.Name, UInt16.Parse(v));
                            break;
                        case TypeCode.UInt32:
                            SetPropertyValue(tag, p.Name, UInt32.Parse(v));
                            break;
                        case TypeCode.Double:
                            SetPropertyValue(tag, p.Name, Double.Parse(v));
                            break;
                        case TypeCode.Byte:
                            SetPropertyValue(tag, p.Name, Byte.Parse(v));
                            break;
                        case TypeCode.Boolean:
                            SetPropertyValue(tag, p.Name, Boolean.Parse(v));
                            break;
                        case TypeCode.String:
                            SetPropertyValue(tag, p.Name, v.ToString());
                            break;
                        default:
                            break;
                    }
                }
            }

            var owner = GetOwnerNode(startingNode, tagBrowsePath);
            var alreadyExistingTag = NodeAlreadyExists(owner, tag);

            if (alreadyExistingTag != null)
            {
                TagsUpdate(alreadyExistingTag, tag);
                tagsUpdated++;
            }
            else
            {
                owner.Add(tag);
                tagsCreated++;
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void SetTagArrayDimensions(FTOptix.CommunicationDriver.Tag tag, string tagArrayLengthString)
    {
        var isDataMatrix = tagArrayLengthString.Contains(arrayLengthSeparator);
        if (isDataMatrix)
        {
            var indexes = tagArrayLengthString.Split(arrayLengthSeparator);
            var index0 = uint.Parse(indexes[0]);
            var index1 = uint.Parse(indexes[1]);
            tag.ArrayDimensions = new uint[] { index0, index1 };
        }
        else
        {
            var index = uint.Parse(tagArrayLengthString);
            tag.ArrayDimensions = new uint[] { index };
        }
    }

    private void TagsUpdate(IUANode destinationTag, FTOptix.CommunicationDriver.Tag sourceTag)
    {
        try
        {
            var destinationValueType = ((UAManagedCore.UAVariable)destinationTag).Value.Value.GetType();
            var sourceValueType = sourceTag.Value.Value.GetType();
            if (destinationValueType != sourceValueType)
            {
                Log.Error("Tag " + destinationTag.BrowseName + " cannot be updated because its type is " + destinationValueType + " and the imported data says " + sourceValueType);
                return;
            }
            var tagProperties = GetPropertiesNamesFromTagType(sourceTag.GetType());
            foreach (var p in tagProperties)
            {
                object v = GetPropertyValue(sourceTag, p);
                if (v == null) continue;
                SetPropertyValue(destinationTag, p, v);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.StackTrace + " " + ex.Message);
        }
    }

    private IUANode NodeAlreadyExists(IUANode tagOwner, IUANode tag) => tagOwner.Children.FirstOrDefault(t => t.BrowseName == tag.BrowseName);

    private IUANode GetOwnerNode(IUANode startingNode, string relativePath)
    {
        var rawPath = relativePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var path = new string[rawPath.Length - 2];
        Array.Copy(rawPath, 1, path, 0, rawPath.Length - 2);
        var tagOwner = startingNode;

        foreach (var nodeName in path) tagOwner = tagOwner.Get(nodeName);

        return tagOwner;
    }

    private int GetElementIndex(string[] array, string key) => Array.IndexOf(array, key);

    private void RetrieveParameters()
    {
        csvFilename = CSV_FILENAME;
        tagsCsvUri = ResourceUri.FromProjectRelativePath(csvFilename).Uri;
        startingNode = InformationModel.Get(LogicObject.GetVariable("StartingNodeToFetch").Value);
    }

    private void WriteTagsToCsv(IUANode startingNode)
    {
        try
        {
            File.Create(tagsCsvUri).Close();
            var tag = GetOneOfTheTags(startingNode);
            var csvHeader = GenerateCsvHeader(tag);
            var tagPropertiesNames = GetTagPropertiesNames(tag);
            var tagsAndStructures = GetTagsAndStructures(startingNode);

            var tags = tagsAndStructures.Item1;
            var tagsStructures = tagsAndStructures.Item2.Where(t => t.ArrayDimensions.Length == 0);
            var tagsStructureArrays = tagsAndStructures.Item2.Where(t => t.ArrayDimensions.Length != 0);

            System.Text.Encoding encoding = System.Text.Encoding.Unicode;

            using (StreamWriter sWriter = new StreamWriter(tagsCsvUri, false, encoding))
            {
                sWriter.WriteLine(csvHeader);
                foreach (var t in tagsStructureArrays) WriteTagOnCsv<FTOptix.CommunicationDriver.TagStructure>(t, tagPropertiesNames, sWriter);
                foreach (var t in tagsStructures) WriteTagOnCsv<FTOptix.CommunicationDriver.TagStructure>(t, tagPropertiesNames, sWriter);
                foreach (var t in tags) WriteTagOnCsv<FTOptix.CommunicationDriver.Tag>(t, tagPropertiesNames, sWriter);
            }

            Log.Info("Tags exported: " + tags.Count());
            Log.Info("Tag structures exported: " + tagsStructures.Count());
            Log.Info("Tag structure arrays exported: " + tagsStructureArrays.Count());
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void WriteTagOnCsv<T>(T t, List<string> tagPropertiesNames, StreamWriter sWriter)
    {
        var tRow = GetPropertyInfoFromTag<T>(t, tagPropertiesNames);
        sWriter.WriteLine(String.Join(csvSeparator, tRow));
    }

    private IUANode GetOneOfTheTags(IUANode startingNode)
    {
        if (startingNode is FTOptix.CommunicationDriver.Tag) return startingNode;
        foreach (var c in startingNode.Children)
        {
            if (c.GetType().Name == typeof(FTOptix.CommunicationDriver.Tag).Name) return c;
            return GetOneOfTheTags(c);
        }
        return null;
    }

    private HashSet<string> GetPropertiesNamesFromTagType(Type type)
    {
        var propertiesNames = new HashSet<string>();
        foreach (PropertyInfo p in GetTypePropertieInfos(type))
        {
            if (p.PropertyType.Name != typeof(IUAVariable).Name) propertiesNames.Add(p.Name);
        }
        return propertiesNames;
    }

    private PropertyInfo[] GetTypePropertieInfos(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private List<string> GetTagPropertiesNames<T>()
    {
        var tagProperties = GetPropertiesNamesFromTagType(typeof(T));
        return ComposeCustomAndNativeTagPropertiesNames(tagProperties);
    }

    private List<string> GetTagPropertiesNames(IUANode t)
    {
        var tagProperties = GetPropertiesNamesFromTagType(t.GetType());
        return ComposeCustomAndNativeTagPropertiesNames(tagProperties);
    }

    private List<string> ComposeCustomAndNativeTagPropertiesNames(HashSet<string> tagProperties)
    {
        tagsPropertiesNames = new List<string>();
        tagsPropertiesNames.AddRange(customTagsPropertiesNames);
        tagsPropertiesNames.AddRange(tagProperties);
        return tagsPropertiesNames;
    }

    private string GenerateCsvHeader<T>() => String.Join(csvSeparator, GetTagPropertiesNames<T>());
    private string GenerateCsvHeader(IUANode t) => String.Join(csvSeparator, GetTagPropertiesNames(t));
    private List<string> GetPropertyInfoFromTag<T>(T t, List<string> tagProperties)
    {
        try
        {
            Dictionary<string, string> tagPropertiesDict = new Dictionary<string, string>();
            tagProperties = tagProperties.Distinct().ToList();

            switch (t)
            {
                case FTOptix.CommunicationDriver.Tag:
                    var imATag = (t as FTOptix.CommunicationDriver.Tag);
                    tagPropertiesDict.Add(tagProperties[0], imATag.GetType().FullName);
                    tagPropertiesDict.Add(tagProperties[1], imATag.BrowseName);
                    tagPropertiesDict.Add(tagProperties[2], GetBrowsePath(startingNode, imATag, "/"));
                    tagPropertiesDict.Add(tagProperties[3], InformationModel.Get(imATag.DataType).BrowseName);
                    var tagArrayDim = imATag.ArrayDimensions.Length == 0 ?
                                            string.Empty
                                            : imATag.ArrayDimensions.Length == 1 ?
                                            imATag.ArrayDimensions[0].ToString()
                                            : imATag.ArrayDimensions[0].ToString() + arrayLengthSeparator + imATag.ArrayDimensions[1].ToString();
                    tagPropertiesDict.Add(tagProperties[4], tagArrayDim);
                    for (int i = 5; i < tagProperties.Count; i++)
                    {
                        var property = tagProperties[i];
                        var propertyVal = GetPropertyValue(imATag, property);
                        tagPropertiesDict.Add(property, propertyVal == null ? string.Empty : propertyVal.ToString());
                    }
                    break;
                case FTOptix.CommunicationDriver.TagStructure:
                    var imATagStructure = (t as FTOptix.CommunicationDriver.TagStructure);
                    tagPropertiesDict.Add(tagProperties[0], imATagStructure.GetType().FullName);
                    tagPropertiesDict.Add(tagProperties[1], imATagStructure.BrowseName);
                    tagPropertiesDict.Add(tagProperties[2], GetBrowsePath(startingNode, imATagStructure, "/"));
                    tagPropertiesDict.Add(tagProperties[3], string.Empty);

                    if (imATagStructure.ArrayDimensions.Length == 0)
                    {
                        tagPropertiesDict.Add(tagProperties[4], string.Empty);
                    }
                    else
                    {
                        var tagStructureArrayDim = imATagStructure.ArrayDimensions[0] == 0 ? string.Empty : imATagStructure.ArrayDimensions[0].ToString();
                        tagPropertiesDict.Add(tagProperties[4], tagStructureArrayDim);
                    }
                    break;
                default:
                    break;
            }

            return tagPropertiesDict.Select(kv => kv.Value).ToList();
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
            return new List<string>();
        }
    }

    private static (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) GetTagsAndStructures(IUANode startingNode)
    {
        var tuple = (new List<FTOptix.CommunicationDriver.Tag>(), new List<FTOptix.CommunicationDriver.TagStructure>());

        foreach (var t in startingNode.Children)
        {
            switch (t)
            {
                case FTOptix.CommunicationDriver.Tag _:
                    tuple.Item1.Add((FTOptix.CommunicationDriver.Tag)t);
                    break;
                case FTOptix.CommunicationDriver.TagStructure _:
                    tuple.Item2.Add((FTOptix.CommunicationDriver.TagStructure)t);
                    tuple = MergeTuples(tuple, (GetTagsAndStructures(t)));
                    break;
                default:
                    tuple = MergeTuples(tuple, (GetTagsAndStructures(t)));
                    break;
            }
        }
        return tuple;
    }

    private static (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) MergeTuples((List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) tuple1, (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) tuple2) => (tuple1.Item1.Concat(tuple2.Item1).ToList(), tuple1.Item2.Concat(tuple2.Item2).ToList());

    private static string GetBrowsePath(IUANode startingNode, IUANode uANode, string sepatator)
    {
        var browsePath = string.Empty;
        var isStartingNode = uANode.NodeId == startingNode.NodeId;

        if (isStartingNode) return startingNode.BrowseName + browsePath;

        return GetBrowsePath(startingNode, uANode.Owner, sepatator) + sepatator + uANode.BrowseName;
    }

    private string GetBrowseName(string[] values, string[] header) => values[GetElementIndex(header, customTagsPropertiesNames[1])];
    private string GetBrowsePath(string[] values, string[] header) => values[GetElementIndex(header, customTagsPropertiesNames[2])];
    private string GetDataTypeString(string[] values, string[] header) => values[GetElementIndex(header, customTagsPropertiesNames[3])];
    private string GetArrayLengthString(string[] values, string[] header) => values[GetElementIndex(header, customTagsPropertiesNames[4])];
    private void SetPropertyValue(FTOptix.CommunicationDriver.Tag tag, PropertyInfo propertyInfo, object val) => SetPropertyValue(tag, propertyInfo.Name, val);
    private void SetPropertyValue(FTOptix.CommunicationDriver.Tag tag, string propertyName, object val) => tag.GetType().GetProperty(propertyName).SetValue(tag, val);
    private void SetPropertyValue(IUANode tag, string propertyName, object val) => tag.GetType().GetProperty(propertyName).SetValue(tag, val);
    private PropertyInfo GetProperty<T>(T tag, string propertyName) => tag.GetType().GetProperty(propertyName);
    private object GetPropertyValue<T>(T tag, string propertyName) => GetProperty(tag, propertyName).GetValue(tag);

    private NodeId GetOpcUaDataType(string tagDataTypeString)
    {
        var tagNetType = GetNetTypeFromOPCUAType(tagDataTypeString);

        switch (Type.GetTypeCode(tagNetType))
        {
            case TypeCode.SByte:
                return OpcUa.DataTypes.SByte;
            case TypeCode.Int16:
                return OpcUa.DataTypes.Int16;
            case TypeCode.Int32:
                return OpcUa.DataTypes.Int32;
            case TypeCode.Int64:
                return OpcUa.DataTypes.Int64;
            case TypeCode.Byte:
                return OpcUa.DataTypes.Byte;
            case TypeCode.UInt16:
                return OpcUa.DataTypes.UInt16;
            case TypeCode.UInt32:
                return OpcUa.DataTypes.UInt32;
            case TypeCode.UInt64:
                return OpcUa.DataTypes.UInt64;
            case TypeCode.Boolean:
                return OpcUa.DataTypes.Boolean;
            case TypeCode.Double:
                return OpcUa.DataTypes.Double;
            case TypeCode.Single:
                return OpcUa.DataTypes.Float;
            case TypeCode.String:
                return OpcUa.DataTypes.String;
            case TypeCode.DateTime:
                return OpcUa.DataTypes.DateTime;
            default:
                return OpcUa.DataTypes.BaseDataType;
        }
    }

    private Type GetNetTypeFromOPCUAType(string dataTypeString)
    {
        var netType = DataTypesHelper.GetNetTypeByDataTypeName(dataTypeString);
        if (netType == null) throw new Exception($"Type corresponding to {dataTypeString} was not found in OPCUA namespace");
        return netType;
    }
}
