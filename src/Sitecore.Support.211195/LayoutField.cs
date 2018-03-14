using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Layouts;
using Sitecore.Links;
using Sitecore.Pipelines;
using Sitecore.Pipelines.GetLayoutSourceFields;
using Sitecore.Pipelines.ResolveRenderingDatasource;
using Sitecore.Text;
using Sitecore.Xml;
using Sitecore.Xml.Patch;

namespace Sitecore.Data.Fields
{

  /// <summary>Represents a Layout field.</summary>
  public class LayoutField : CustomField
  {
    #region Constants and Fields

    /// <summary>
    /// Specifies empty value for the layout field.
    /// </summary>
    public const string EmptyValue = "<r />";

    /// <summary>The data.</summary>
    private readonly XmlDocument data;

    #endregion

    #region Constructors and Destructors

    /// <summary>Initializes a new instance of the <see cref="LayoutField"/> class. Creates LayoutField from specific item.</summary>
    /// <param name="item">Item to get layout for.</param>
    public LayoutField([NotNull] Item item)
      : this(item.Fields[FieldIDs.FinalLayoutField])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LayoutField"/> class. Creates a new <see cref="LayoutField"/> instance.</summary>
    /// <param name="innerField">Inner field.</param>
    public LayoutField([NotNull] Field innerField)
      : base(innerField)
    {
      Assert.ArgumentNotNull(innerField, "innerField");

      this.data = this.LoadData();
    }

    /// <summary>Initializes a new instance of the <see cref="LayoutField"/> class.</summary>
    /// <param name="innerField">The inner field.</param>
    /// <param name="runtimeValue">The runtime value.</param>
    public LayoutField([NotNull] Field innerField, [NotNull] string runtimeValue)
      : base(innerField, runtimeValue)
    {
      Assert.ArgumentNotNull(innerField, "innerField");
      Assert.ArgumentNotNullOrEmpty(runtimeValue, "runtimeValue");

      this.data = this.LoadData();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the XML data document.
    /// </summary>
    /// <value>The data.</value>
    [NotNull]
    public XmlDocument Data
    {
      get
      {
        return this.data;
      }
    }

    #endregion

    #region Operators

    /// <summary>
    /// Converts a <see cref="Field"/> to a <see cref="LayoutField"/>.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The implicit operator.</returns>
    public static implicit operator LayoutField([CanBeNull] Field field)
    {
      if (field != null)
      {
        return new LayoutField(field);
      }

      return null;
    }

    #endregion

    #region Public Methods

    /// <summary>Extracts the layout ID.</summary>
    /// <param name="deviceNode">Device node.</param>
    /// <returns>The layout ID.</returns>
    [NotNull]
    public static ID ExtractLayoutID([NotNull] XmlNode deviceNode)
    {
      Assert.ArgumentNotNull(deviceNode, "deviceNode");

      string value = XmlUtil.GetAttribute("l", deviceNode);

      if (value.Length > 0 && ID.IsID(value))
      {
        return ID.Parse(value);
      }

      return ID.Null;
    }

    /// <summary>Extracts the Rendering references.</summary>
    /// <param name="deviceNode">Device node.</param>
    /// <param name="language">Language.</param>
    /// <param name="database">Database.</param>
    /// <returns>The references.</returns>
    [NotNull]
    public static RenderingReference[] ExtractReferences(
      [NotNull] XmlNode deviceNode, [NotNull] Language language, [NotNull] Database database)
    {
      Assert.ArgumentNotNull(deviceNode, "deviceNode");
      Assert.ArgumentNotNull(language, "language");
      Assert.ArgumentNotNull(database, "database");

      XmlNodeList nodes = deviceNode.SelectNodes("r");
      Assert.IsNotNull(nodes, "nodes");

      var references = new RenderingReference[nodes.Count];

      for (int n = 0; n < nodes.Count; n++)
      {
        references[n] = new RenderingReference(nodes[n], language, database);
      }

      return references;
    }

    /// <summary>Gets the field value, applying any layout deltas.</summary>
    /// <param name="field">The field to get value for.</param>
    /// <returns>The calculated layout value.</returns>
    [NotNull]
    public static string GetFieldValue([NotNull] Field field)
    {
      Assert.ArgumentNotNull(field, "field");
      Assert.IsTrue(field.ID == FieldIDs.LayoutField || field.ID == FieldIDs.FinalLayoutField, "The field is not a layout/renderings field");

      var args = new GetLayoutSourceFieldsArgs(field);
      bool success = GetLayoutSourceFieldsPipeline.Run(args);

      var fieldValues = new List<string>();


      if (success)
      {
        // fieldValue.GetValue(false, false, true, true) does not fit requirements since it uses fieldValue.GetInheritedValue(true) under the hood
        fieldValues.AddRange(args.FieldValuesSource.Select(fieldValue => fieldValue.GetValue(false, false) ?? fieldValue.GetInheritedValue(false) ?? fieldValue.GetValue(false, false, allowFallbackValue: true, allowInheritValue: false, allowInnerValue: false)));
        fieldValues.AddRange(args.StandardValuesSource.Select(fieldValue => fieldValue.GetStandardValue()));
      }
      else
      {
        fieldValues = DoGetFieldValue(field);
      }

      var stackPatches = new Stack<string>();
      string baseXml = null;

      foreach (string value in fieldValues)
      {
        if (string.IsNullOrWhiteSpace(value))
        {
          continue;
        }

        if (XmlPatchUtils.IsXmlPatch(value))
        {
          stackPatches.Push(value);
          continue;
        }

        baseXml = value;
        break;
      }

      if (string.IsNullOrWhiteSpace(baseXml))
      {
        return string.Empty;
      }

      // patching
      return stackPatches.Aggregate(baseXml, XmlDeltas.ApplyDelta);
    }

    /// <summary>Sets the field value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static void SetFieldValue([NotNull] Field field, [NotNull] string value)
    {
      Assert.ArgumentNotNull(field, "field");
      Assert.ArgumentNotNull(value, "value");
      Assert.IsTrue(field.ID == FieldIDs.LayoutField || field.ID == FieldIDs.FinalLayoutField, "The field is not a layout/renderings field");

      string baseXml = null;
      bool isStandardValues = field.Item.Name == Constants.StandardValuesItemName;
      bool isShared = field.ID == FieldIDs.LayoutField;

      Field baseField;

      // find base field
      if (isStandardValues && isShared)
      {
        baseField = null;
      }
      else if (isStandardValues)
      {
        baseField = field.Item.Fields[FieldIDs.LayoutField];
      }
      else if (isShared)
      {
        TemplateItem template = field.Item.Template;
        baseField = (template != null && template.StandardValues != null) ? template.StandardValues.Fields[FieldIDs.FinalLayoutField] : null;
      }
      else
      {
        baseField = field.Item.Fields[FieldIDs.LayoutField];
      }

      if (baseField != null)
      {
        baseXml = GetFieldValue(baseField);
      }

      if (XmlUtil.XmlStringsAreEqual(value, baseXml))
      {
        field.Reset();
        return;
      }

      if (!string.IsNullOrWhiteSpace(baseXml))
      {
        field.Value = XmlDeltas.GetDelta(value, baseXml);
      }
      else
      {
        field.Value = value;
      }
    }

    /// <summary>
    /// Sets the field value.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="baseValue">The base value.</param>
    public static void SetFieldValue([NotNull] Field field, [NotNull] string value, [NotNull] string baseValue)
    {
      Assert.ArgumentNotNull(field, "field");
      Assert.ArgumentNotNull(value, "value");
      Assert.ArgumentNotNull(baseValue, "baseValue");
      Assert.IsTrue(field.ID == FieldIDs.LayoutField || field.ID == FieldIDs.FinalLayoutField, "The field is not a layout/renderings field");

      string result;

      if (XmlUtil.XmlStringsAreEqual(value, baseValue))
      {
        field.Reset();
        return;
      }

      if (!string.IsNullOrWhiteSpace(baseValue))
      {
        result = XmlDeltas.GetDelta(value, baseValue);
      }
      else
      {
        result = value;
      }

      if (!XmlUtil.XmlStringsAreEqual(XmlDeltas.ApplyDelta(baseValue, field.Value), XmlDeltas.ApplyDelta(baseValue, result)))
      {
        field.Value = result;
      }
    }

    /// <summary>Gets the device node.</summary>
    /// <param name="device">Device.</param>
    /// <returns>The device node.</returns>
    /// <contract>
    ///   <requires name="device" condition="none"/>
    /// </contract>
    [CanBeNull]
    public XmlNode GetDeviceNode([CanBeNull] DeviceItem device)
    {
      if (device != null)
      {
        return this.Data.DocumentElement.SelectSingleNode("d[@id='" + device.ID + "']");
      }

      return null;
    }

    /// <summary>Gets the layout ID.</summary>
    /// <param name="device">Device.</param>
    /// <returns>The layout ID.</returns>
    /// <contract>
    ///   <requires name="device" condition="none"/>
    /// </contract>
    [NotNull]
    public ID GetLayoutID([NotNull] DeviceItem device)
    {
      Assert.ArgumentNotNull(device, "device");

      XmlNode deviceNode = this.GetDeviceNode(device);

      if (deviceNode != null)
      {
        return ExtractLayoutID(deviceNode);
      }

      return ID.Null;
    }

    /// <summary>Gets the Rendering references for a device.</summary>
    /// <param name="device">Device.</param>
    /// <returns>The references.</returns>
    /// <contract>
    ///   <requires name="device" condition="none"/>
    /// </contract>
    [CanBeNull]
    public RenderingReference[] GetReferences([NotNull] DeviceItem device)
    {
      Assert.ArgumentNotNull(device, "device");

      XmlNode deviceNode = this.GetDeviceNode(device);

      if (deviceNode != null)
      {
        return ExtractReferences(deviceNode, this.InnerField.Language, this.InnerField.Database);
      }

      return null;
    }

    /// <summary>Relinks the specified item.</summary>
    /// <param name="itemLink">The item link.</param>
    /// <param name="newLink">The new link.</param>
    public override void Relink([NotNull] ItemLink itemLink, [NotNull] Item newLink)
    {
      Assert.ArgumentNotNull(itemLink, "itemLink");
      Assert.ArgumentNotNull(newLink, "newLink");

      string value = this.Value;
      if (string.IsNullOrEmpty(value))
      {
        return;
      }

      LayoutDefinition layoutDefinition = LayoutDefinition.Parse(value);

      ArrayList devices = layoutDefinition.Devices;
      if (devices == null)
      {
        return;
      }

      string targetItemID = itemLink.TargetItemID.ToString();
      string newLinkID = newLink.ID.ToString();

      for (int n = devices.Count - 1; n >= 0; n--)
      {
        var device = devices[n] as DeviceDefinition;
        if (device == null)
        {
          continue;
        }

        if (device.ID == targetItemID)
        {
          device.ID = newLinkID;
          continue;
        }

        if (device.Layout == targetItemID)
        {
          device.Layout = newLinkID;
          continue;
        }

        if (device.Placeholders != null)
        {
          string targetPath = itemLink.TargetPath;
          bool isLinkFound = false;
          for (int j = device.Placeholders.Count - 1; j >= 0; j--)
          {
            var placeholderDefinition = device.Placeholders[j] as PlaceholderDefinition;
            if (placeholderDefinition == null)
            {
              continue;
            }

            if (
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetPath, StringComparison.InvariantCultureIgnoreCase) ||
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetItemID, StringComparison.InvariantCultureIgnoreCase))
            {
              placeholderDefinition.MetaDataItemId = newLink.Paths.FullPath;
              isLinkFound = true;
            }
          }

          if (isLinkFound)
          {
            continue;
          }
        }

        if (device.Renderings == null)
        {
          continue;
        }

        for (int r = device.Renderings.Count - 1; r >= 0; r--)
        {
          var rendering = device.Renderings[r] as RenderingDefinition;
          if (rendering == null)
          {
            continue;
          }

          if (rendering.ItemID == targetItemID)
          {
            rendering.ItemID = newLinkID;
          }

          if (rendering.Datasource == targetItemID)
          {
            rendering.Datasource = newLinkID;
          }

          if (rendering.Datasource == itemLink.TargetPath)
          {
            rendering.Datasource = newLink.Paths.FullPath;
          }

          if (!string.IsNullOrEmpty(rendering.Parameters))
          {
            Item layoutItem = this.InnerField.Database.GetItem(rendering.ItemID);

            if (layoutItem == null)
            {
              continue;
            }

            var renderingParametersFieldCollection = this.GetParametersFields(layoutItem, rendering.Parameters);

            foreach (var field in renderingParametersFieldCollection.Values)
            {
              if (!string.IsNullOrEmpty(field.Value))
              {
                field.Relink(itemLink, newLink);
              }
            }

            rendering.Parameters = renderingParametersFieldCollection.GetParameters().ToString();
          }

          if (rendering.Rules != null)
          {
            var rulesField = new RulesField(this.InnerField, rendering.Rules.ToString());
            rulesField.Relink(itemLink, newLink);
            rendering.Rules = XElement.Parse(rulesField.Value);
          }
        }
      }

      this.Value = layoutDefinition.ToXml();
    }

    /// <summary>Removes the link.</summary>
    /// <param name="itemLink">The item link.</param>
    public override void RemoveLink([NotNull] ItemLink itemLink)
    {
      Assert.ArgumentNotNull(itemLink, "itemLink");

      string value = this.Value;
      if (string.IsNullOrEmpty(value))
      {
        return;
      }

      LayoutDefinition layoutDefinition = LayoutDefinition.Parse(value);
      ArrayList devices = layoutDefinition.Devices;
      if (devices == null)
      {
        return;
      }

      string targetItemID = itemLink.TargetItemID.ToString();

      for (int n = devices.Count - 1; n >= 0; n--)
      {
        var device = devices[n] as DeviceDefinition;
        if (device == null)
        {
          continue;
        }

        if (device.ID == targetItemID)
        {
          devices.Remove(device);
          continue;
        }

        if (device.Layout == targetItemID)
        {
          device.Layout = null;
          continue;
        }

        if (device.Placeholders != null)
        {
          string targetPath = itemLink.TargetPath;
          bool isLinkFound = false;
          for (int j = device.Placeholders.Count - 1; j >= 0; j--)
          {
            var placeholderDefinition = device.Placeholders[j] as PlaceholderDefinition;
            if (placeholderDefinition == null)
            {
              continue;
            }

            if (
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetPath, StringComparison.InvariantCultureIgnoreCase) ||
              string.Equals(
                placeholderDefinition.MetaDataItemId, targetItemID, StringComparison.InvariantCultureIgnoreCase))
            {
              device.Placeholders.Remove(placeholderDefinition);
              isLinkFound = true;
            }
          }

          if (isLinkFound)
          {
            continue;
          }
        }

        if (device.Renderings == null)
        {
          continue;
        }

        for (int r = device.Renderings.Count - 1; r >= 0; r--)
        {
          var rendering = device.Renderings[r] as RenderingDefinition;
          if (rendering == null)
          {
            continue;
          }

          if (rendering.Datasource == itemLink.TargetPath)
          {
            rendering.Datasource = string.Empty;
          }

          if (rendering.ItemID == targetItemID)
          {
            device.Renderings.Remove(rendering);
          }

          if (rendering.Datasource == targetItemID)
          {
            rendering.Datasource = string.Empty;
          }

          if (!string.IsNullOrEmpty(rendering.Parameters))
          {
            Item layoutItem = this.InnerField.Database.GetItem(rendering.ItemID);

            if (layoutItem == null)
            {
              continue;
            }

            var renderingParametersFieldCollection = this.GetParametersFields(layoutItem, rendering.Parameters);

            foreach (var field in renderingParametersFieldCollection.Values)
            {
              if (!string.IsNullOrEmpty(field.Value))
              {
                field.RemoveLink(itemLink);
              }
            }

            rendering.Parameters = renderingParametersFieldCollection.GetParameters().ToString();
          }

          if (rendering.Rules != null)
          {
            var rulesField = new RulesField(this.InnerField, rendering.Rules.ToString());
            rulesField.RemoveLink(itemLink);
            rendering.Rules = XElement.Parse(rulesField.Value);
          }
        }
      }

      this.Value = layoutDefinition.ToXml();
    }

    /// <summary>Validates the links.</summary>
    /// <param name="result">The result.</param>
    public override void ValidateLinks([NotNull] LinksValidationResult result)
    {
      Assert.ArgumentNotNull(result, "result");

      string value = this.Value;
      if (string.IsNullOrEmpty(value))
      {
        return;
      }

      LayoutDefinition layoutDefinition = LayoutDefinition.Parse(value);

      ArrayList devices = layoutDefinition.Devices;
      if (devices == null)
      {
        return;
      }

      foreach (DeviceDefinition device in devices)
      {
        if (!string.IsNullOrEmpty(device.ID))
        {
          Item deviceItem = this.InnerField.Database.GetItem(device.ID);

          if (deviceItem != null)
          {
            result.AddValidLink(deviceItem, device.ID);
          }
          else
          {
            result.AddBrokenLink(device.ID);
          }
        }

        if (!string.IsNullOrEmpty(device.Layout))
        {
          Item layoutItem = this.InnerField.Database.GetItem(device.Layout);

          if (layoutItem != null)
          {
            result.AddValidLink(layoutItem, device.Layout);
          }
          else
          {
            result.AddBrokenLink(device.Layout);
          }
        }

        this.ValidatePlaceholderSettings(result, device);

        if (device.Renderings == null)
        {
          continue;
        }

        foreach (RenderingDefinition rendering in device.Renderings)
        {
          if (rendering.ItemID == null)
          {
            continue;
          }

          Item renderingItem = this.InnerField.Database.GetItem(rendering.ItemID);

          if (renderingItem != null)
          {
            result.AddValidLink(renderingItem, rendering.ItemID);
          }
          else
          {
            result.AddBrokenLink(rendering.ItemID);
          }

          string datasource = rendering.Datasource;
          if (!string.IsNullOrEmpty(datasource))
          {
            using (new ContextItemSwitcher(this.InnerField.Item))
            {
              var args = new ResolveRenderingDatasourceArgs(datasource);
              CorePipeline.Run("resolveRenderingDatasource", args, false);
              datasource = args.Datasource;
            }

            Item dataSourceItem = this.InnerField.Database.GetItem(datasource);
            if (dataSourceItem != null)
            {
              result.AddValidLink(dataSourceItem, datasource);
            }
            else
            {
              if (!datasource.Contains(":"))
              {
                result.AddBrokenLink(datasource);
              }
            }
          }

          string mvTest = rendering.MultiVariateTest;
          if (!string.IsNullOrEmpty(mvTest))
          {
            Item testDefinitionItem = this.InnerField.Database.GetItem(mvTest);
            if (testDefinitionItem != null)
            {
              result.AddValidLink(testDefinitionItem, mvTest);
            }
            else
            {
              result.AddBrokenLink(mvTest);
            }
          }

          string personalizationTest = rendering.PersonalizationTest;
          if (!string.IsNullOrEmpty(personalizationTest))
          {
            Item testDefinitionItem = this.InnerField.Database.GetItem(personalizationTest);
            if (testDefinitionItem != null)
            {
              result.AddValidLink(testDefinitionItem, personalizationTest);
            }
            else
            {
              result.AddBrokenLink(personalizationTest);
            }
          }

          if (renderingItem != null && !string.IsNullOrEmpty(rendering.Parameters))
          {
            var renderingParametersFieldCollection = this.GetParametersFields(renderingItem, rendering.Parameters);

            foreach (var field in renderingParametersFieldCollection.Values)
            {
              field.ValidateLinks(result);
            }
          }

          if (rendering.Rules != null)
          {
            var rulesField = new RulesField(this.InnerField, rendering.Rules.ToString());
            rulesField.ValidateLinks(result);
          }
        }
      }
    }

    /// <summary>
    /// Fallback method that used in case <code>getLayoutSourceFields</code> pipeline is not defined.
    /// </summary>
    /// <param name="field">The field to get value for.</param>
    /// <returns>List of values to use as a source for field value.</returns>
    [NotNull]
    [Obsolete("Use GetLayoutSourceFieldsPipeline.Run(GetLayoutSourceFieldsArgs args) method instead.")]
    private static List<string> DoGetFieldValue([NotNull] Field field)
    {
      Debug.ArgumentNotNull(field, "field");

      var item = field.Item;
      var fields = item.Fields;

      IEnumerable<Lazy<string>> fieldValues = new[]
      {
        // versioned field of the item
        new Lazy<string>(() => fields[FieldIDs.FinalLayoutField].GetValue(false, false) ?? fields[FieldIDs.FinalLayoutField].GetInheritedValue(false)),

        // shared field of the item
        new Lazy<string>(() => fields[FieldIDs.LayoutField].GetValue(false, false) ?? fields[FieldIDs.LayoutField].GetInheritedValue(false)), 

        // versioned field of the Standard values
        new Lazy<string>(() => fields[FieldIDs.FinalLayoutField].GetStandardValue()),

        // shared field of the Standard values
        new Lazy<string>(() => fields[FieldIDs.LayoutField].GetStandardValue())
      };

      bool isStandardValues = item.Name == Constants.StandardValuesItemName;

      bool isShared = field.ID == FieldIDs.LayoutField;
      if (isStandardValues && isShared)
      {
        fieldValues = fieldValues.Skip(3);
      }
      else if (isStandardValues)
      {
        fieldValues = fieldValues.Skip(2);
      }
      else if (isShared)
      {
        fieldValues = fieldValues.Skip(1);
      }

      return fieldValues.Select(x => x.Value).ToList();
    }

    /// <summary>
    /// Gets the parameters fields.
    /// </summary>
    /// <param name="layoutItem">The layout item.</param>
    /// <param name="renderingParameters">The rendering parameters.</param>
    /// <returns></returns>
    private RenderingParametersFieldCollection GetParametersFields(Item layoutItem, string renderingParameters)
    {
      var urlParametersString = new UrlString(renderingParameters);
      RenderingParametersFieldCollection parametersFields;

      //layoutItem.Template.CreateItemFrom()

      RenderingParametersFieldCollection.TryParse(layoutItem, urlParametersString, out parametersFields);

      return parametersFields;
    }

    #endregion

    #region Methods

    /// <summary>Sets the layout hack.</summary>
    /// <param name="value">The value.</param>
    /// <contract>
    /// 	<requires name="value" condition="not null"/>
    /// </contract>
    internal void SetLayoutHack([NotNull] string value)
    {
      Assert.ArgumentNotNull(value, "value");

      XmlNodeList nodes = this.Data.DocumentElement.SelectNodes("d");
      Assert.IsNotNull(nodes, "nodes");

      if (nodes.Count > 0)
      {
        foreach (XmlNode node in nodes)
        {
          XmlUtil.SetAttribute("l", value, node);
        }

        this.Value = this.Data.OuterXml;
      }
    }

    /// <summary>Gets the actual value of this field.</summary>
    /// <returns>Actual value of this field object</returns>
    [NotNull]
    protected override string GetValue()
    {
      if (this._hasRuntimeValue)
      {
        return this._runtimeValue;
      }

      return GetFieldValue(this._innerField);
    }

    /// <summary>
    /// Sets the value of this field.
    /// </summary>
    /// <param name="value">Value to set.</param>
    protected override void SetValue([NotNull] string value)
    {
      Assert.ArgumentNotNull(value, "value");

      if (this._hasRuntimeValue)
      {
        this._runtimeValue = value;
      }

      SetFieldValue(this._innerField, value);
    }

    /// <summary>Validates the placeholder settings.</summary>
    /// <param name="result">The result.</param>
    /// <param name="device">The device.</param>
    protected virtual void ValidatePlaceholderSettings(
      [NotNull] LinksValidationResult result, [NotNull] DeviceDefinition device)
    {
      Assert.ArgumentNotNull(result, "result");
      Assert.ArgumentNotNull(device, "device");

      ArrayList placeholders = device.Placeholders;
      if (placeholders != null)
      {
        foreach (PlaceholderDefinition placeholder in placeholders)
        {
          if (placeholder != null && !string.IsNullOrEmpty(placeholder.MetaDataItemId))
          {
            Item settingsItem = this.InnerField.Database.GetItem(placeholder.MetaDataItemId);
            if (settingsItem != null)
            {
              result.AddValidLink(settingsItem, placeholder.MetaDataItemId);
            }
            else
            {
              result.AddBrokenLink(placeholder.MetaDataItemId);
            }
          }
        }
      }
    }

    /// <summary>Loads the data.</summary>
    /// <returns>The data.</returns>
    [NotNull]
    private XmlDocument LoadData()
    {
      string xml = this.Value;

      if (!string.IsNullOrEmpty(xml))
      {
        return XmlUtil.LoadXml(xml);
      }

      return XmlUtil.LoadXml("<r/>");
    }

    #endregion
  }
}