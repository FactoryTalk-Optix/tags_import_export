#region Using directives
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.CommunicationDriver;
using System.Linq;
using FTOptix.Alarm;
#endregion

public class FromPLCToModel : BaseNetLogic
{
    private UAValue setDynamicLinks;
    private Folder modelFolder;

    [ExportMethod]
    public void GenerateNodesIntoModel()
    {
        setDynamicLinks = LogicObject.GetVariable("SetDynamicLinks").Value;
        modelFolder = Project.Current.Get<Folder>("Model");
        var startingNode = InformationModel.Get<TagStructure>(LogicObject.GetVariable("InputNode").Value);
        CreateModelTag(startingNode, modelFolder);
        CheckDatabinds();
    }

    private void CreateModelTag(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        switch (fieldNode)
        {
            case TagStructure _:
                if (!IsTagStructureArray(fieldNode))
                {
                    CreateOrUpdateObject(fieldNode, parentNode, browseNamePrefix);
                }
                else
                {
                    CreateOrUpdateObjectArray(fieldNode, parentNode, browseNamePrefix);
                }
                break;
            default:
                CreateOrUpdateVariable(fieldNode, parentNode, browseNamePrefix);
                break;
        }
    }

    static private bool IsTagStructureArray(IUANode fieldNode) => ((TagStructure)fieldNode).ArrayDimensions.Length != 0;

    private void CreateOrUpdateObjectArray(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        var tagStructureArrayTemp = (TagStructure)fieldNode;
        foreach (var c in tagStructureArrayTemp.Children.Where(c => !IsArrayDimentionsVar(c)))
        {
            CreateModelTag(c, parentNode, fieldNode.BrowseName + "_");
        }
    }

    private void CreateOrUpdateObject(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        var existingNode = GetChild(fieldNode, parentNode, browseNamePrefix);
        // Replacing "/" with "_". Nodes with browsename "/" are not allowed
        var filedNodeBrowseName = fieldNode.BrowseName.Replace("/", "_");

        if (existingNode == null)
        {
            existingNode = InformationModel.MakeObject(browseNamePrefix + filedNodeBrowseName);
            parentNode.Add(existingNode);
        }

        foreach (var t in fieldNode.Children.Where(c => !IsArrayDimentionsVar(c)))
        {
            CreateModelTag(t, existingNode);
        }
    }

    private void CreateOrUpdateVariable(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        if (IsArrayDimentionsVar(fieldNode)) return;
        var existingNode = GetChild(fieldNode, parentNode, browseNamePrefix);

        if (existingNode == null)
        {
            var mTag = (IUAVariable)fieldNode;
            // Replacing "/" with "_". Nodes with browsename "/" are not allowed
            var tagBrowseName = mTag.BrowseName.Replace("/", "_");
            existingNode = InformationModel.MakeVariable(tagBrowseName, mTag.DataType, mTag.ArrayDimensions);
            parentNode.Add(existingNode);
        }
        if (!setDynamicLinks) return;
        ((IUAVariable)existingNode).SetDynamicLink((UAVariable)fieldNode, FTOptix.CoreBase.DynamicLinkMode.ReadWrite);
    }

    private bool IsArrayDimentionsVar(IUANode n) => n.BrowseName.ToLower().Contains("arraydimen");

    private IUANode GetChild(IUANode child, IUANode parent, string browseNamePrefix = "") => parent.Children.FirstOrDefault(c => c.BrowseName == browseNamePrefix + child.BrowseName);

    private void CheckDatabinds()
    {
        var lDataBinds = modelFolder.FindNodesByType<IUAVariable>().Where<IUAVariable>(v => { return v.BrowseName == "DynamicLink"; });
        foreach (var vDataBind in lDataBinds)
        {
            var IsResolved = LogicObject.Context.ResolvePath(vDataBind.Owner, vDataBind.Value).ResolvedNode;
            if (IsResolved == null) { Log.Info($"{Log.Node(vDataBind.Owner)} has unresolved databind"); }
        }
    }
}

