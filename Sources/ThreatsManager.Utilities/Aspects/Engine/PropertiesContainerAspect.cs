﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using PostSharp.Aspects;
using PostSharp.Aspects.Advices;
using PostSharp.Reflection;
using PostSharp.Serialization;
using ThreatsManager.Interfaces.ObjectModel;
using ThreatsManager.Interfaces.ObjectModel.Properties;
using IProperty = ThreatsManager.Interfaces.ObjectModel.Properties.IProperty;

namespace ThreatsManager.Utilities.Aspects.Engine
{
    //#region Additional placeholders required.
    //private IPropertiesContainer PropertiesContainer => this;
    //private List<IProperty> _properties { get; set; }
    //#endregion    

    [PSerializable]
    public class PropertiesContainerAspect : InstanceLevelAspect
    {
        #region Imports from the extended class.
        [ImportMember("Model", IsRequired = true)]
        public Property<IThreatModel> Model;

        [ImportMember("PropertiesContainer", IsRequired = true)]
        public Property<IPropertiesContainer> PropertiesContainer;
        #endregion

        #region Extra elements to be added.

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail,
            LinesOfCodeAvoided = 1, Visibility = Visibility.Private)]
        [CopyCustomAttributes(typeof(JsonPropertyAttribute),
            OverrideAction = CustomAttributeOverrideAction.MergeReplaceProperty)]
        [JsonProperty("properties")]
        public List<IProperty> _properties { get; set; }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail,
            LinesOfCodeAvoided = 1, Visibility = Visibility.Private)]
        public void OnPropertyChanged(IProperty property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            _propertyValueChanged?.Invoke(PropertiesContainer?.Get(), property);
        }

        #endregion

        #region Implementation of interface IPropertiesContainer.
        private Action<IPropertiesContainer, IProperty> _propertyAdded;

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 6)]
        public event Action<IPropertiesContainer, IProperty> PropertyAdded
        {
            add
            {
                if (_propertyAdded == null || !_propertyAdded.GetInvocationList().Contains(value))
                {
                    _propertyAdded += value;
                }
            }
            remove { _propertyAdded -= value; }
        }

        private Action<IPropertiesContainer, IProperty> _propertyRemoved;

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 6)]
        public event Action<IPropertiesContainer, IProperty> PropertyRemoved
        {
            add
            {
                if (_propertyRemoved == null || !_propertyRemoved.GetInvocationList().Contains(value))
                {
                    _propertyRemoved += value;
                }
            }
            remove { _propertyRemoved -= value; }
        }

        private Action<IPropertiesContainer, IProperty> _propertyValueChanged;

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 6)]
        public event Action<IPropertiesContainer, IProperty> PropertyValueChanged
        {
            add
            {
                if (_propertyValueChanged == null || !_propertyValueChanged.GetInvocationList().Contains(value))
                {
                    _propertyValueChanged += value;
                }
            }
            remove { _propertyValueChanged -= value; }
        }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 1)]
        public IEnumerable<IProperty> Properties => _properties?.AsReadOnly();

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 3)]
        public bool HasProperty(IPropertyType propertyType)
        {
            if (propertyType == null)
                throw new ArgumentNullException(nameof(propertyType));

            return _properties?.Any(x => x.PropertyTypeId == propertyType.Id) ?? false;
        }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 3)]
        public IProperty GetProperty(IPropertyType propertyType)
        {
            if (propertyType == null)
                throw new ArgumentNullException(nameof(propertyType));

            return _properties?.FirstOrDefault(x => x.PropertyTypeId == propertyType.Id);
        }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 20)]
        public IProperty AddProperty(IPropertyType propertyType, string value)
        {
            var model = Model?.Get();

            IProperty result = null;
            if (model != null)
            {
                result = InternalAddProperty(model, propertyType, value);

                if (result != null)
                {
                    var schema = model.GetSchema(propertyType.SchemaId);
                    if (schema != null)
                    {
                        schema.PropertyTypeAdded += OnPropertyTypeAdded;
                        schema.PropertyTypeRemoved += OnPropertyTypeRemoved;
                    }
                }
            }

            return result;
        }

        private IProperty InternalAddProperty(IThreatModel model, IPropertyType propertyType, string value)
        {
            IProperty result = null;

            var associatedClass = propertyType.GetType().GetCustomAttributes<AssociatedPropertyClassAttribute>()
                .FirstOrDefault();
            if (associatedClass != null)
            {
                var associatedClassType = Type.GetType(associatedClass.AssociatedType, false);
                if (associatedClassType != null)
                {
                    result = Activator.CreateInstance(associatedClassType, model, propertyType) as IProperty;
                }
            }

            if (result != null)
            {
                if (_properties == null)
                    _properties = new List<IProperty>();
                result.StringValue = value;
                _properties.Add(result);
                Dirty.IsDirty = true;
                _propertyAdded?.Invoke(PropertiesContainer?.Get(), result);
                result.Changed += OnPropertyChanged;
            }

            return result;
        }

        private void OnPropertyTypeRemoved(IPropertySchema schema, IPropertyType propertyType)
        {
            RemoveProperty(propertyType);
        }

        private void OnPropertyTypeAdded(IPropertySchema schema, IPropertyType propertyType)
        {
            var model = Model?.Get();
            if (model != null && schema != null && propertyType != null && !HasProperty(propertyType))
            {
                if (_properties?.Any(x => x.PropertyType != null && x.PropertyType.SchemaId == schema.Id) ?? false)
                    InternalAddProperty(model, propertyType, null);
            }
        }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 10)]
        public bool RemoveProperty(IPropertyType propertyType)
        {
            if (propertyType == null)
                throw new ArgumentNullException(nameof(propertyType));

            bool result = false;

            var property = GetProperty(propertyType);
            if (property != null)
            {
                result = _properties?.Remove(property) ?? false;
                if (result)
                {
                    Dirty.IsDirty = true;
                    _propertyRemoved?.Invoke(PropertiesContainer?.Get(), property);
                }
            }

            return result;
        }

        [IntroduceMember(OverrideAction = MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 9)]
        public bool RemoveProperty(Guid propertyTypeId)
        {
            bool result = false;

            var properties = _properties?.Where(x => x.PropertyTypeId == propertyTypeId).ToArray();
            if (properties?.Any() ?? false) 
            {
                foreach (var property in properties)
                {
                    if (_properties.Remove(property))
                    {
                        _propertyRemoved?.Invoke(PropertiesContainer?.Get(), property);
                        result = true;
                    }
                }

                if (result)
                {
                    Dirty.IsDirty = true;
                }
            }

            return result;
        }
        #endregion

        [IntroduceMember(OverrideAction=MemberOverrideAction.OverrideOrFail, LinesOfCodeAvoided = 7, 
            Visibility = Visibility.Assembly)]
        [CopyCustomAttributes(typeof(OnDeserializedAttribute))]
        [OnDeserialized]
        public void PostDeserialization(StreamingContext context)
        {
            var schemas = _properties?
                .Select(x => x.PropertyType)
                .Where(x => x != null)
                .Select(x => x.SchemaId)
                .Distinct();

            if (schemas?.Any() ?? false)
            {
                var model = Model.Get();
                if (model != null)
                {
                    foreach (var schemaId in schemas)
                    {
                        var schema = model.GetSchema(schemaId);
                        schema.PropertyTypeAdded += OnPropertyTypeAdded;
                        schema.PropertyTypeRemoved += OnPropertyTypeRemoved;
                    }
                }
            }
        }
    }
}
