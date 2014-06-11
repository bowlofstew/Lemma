﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GeeUI.ViewLayouts;
using GeeUI.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using System.Collections;
using Microsoft.Xna.Framework.Input;
using System.Xml.Serialization;
using Lemma.Util;
using ComponentBind;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using View = GeeUI.Views.View;

namespace Lemma.Components
{
	public class EditorGeeUI : Component<Main>
	{
		public struct PopupCommand
		{
			public string Description;
			public PCInput.Chord Chord;
			public Command Action;
			public Func<bool> Enabled;
		}

		private const float precisionDelta = 0.025f;
		private const float normalDelta = 1.0f;
		private const float stringNavigateInterval = 0.08f;

		[XmlIgnore]
		public View RootEditorView;

		[XmlIgnore]
		public TabHost ComponentTabViews;

		[XmlIgnore]
		public PanelView ActionsPanelView;

		[XmlIgnore]
		public DropDownView CreateDropDownView;

		[XmlIgnore]
		public ListProperty<Entity> SelectedEntities = new ListProperty<Entity>();
		[XmlIgnore]
		public Property<bool> MapEditMode = new Property<bool>();
		[XmlIgnore]
		public Property<bool> EnablePrecision = new Property<bool>();

		[XmlIgnore]
		public ListProperty<PopupCommand> PopupCommands = new ListProperty<PopupCommand>();

		[XmlIgnore]
		public Property<bool> NeedsSave = new Property<bool>();


		private SpriteFont MainFont;
		public override void Awake()
		{
			base.Awake();
			MainFont = main.Content.Load<SpriteFont>("EditorFont");

			this.RootEditorView = new View(main.GeeUI, main.GeeUI.RootView);
			this.ComponentTabViews = new TabHost(main.GeeUI, RootEditorView, Vector2.Zero, MainFont);
			this.ActionsPanelView = new PanelView(main.GeeUI, RootEditorView, Vector2.Zero);
			var dropDownPlusLabel = new View(main.GeeUI, ActionsPanelView);
			dropDownPlusLabel.ChildrenLayouts.Add(new VerticalViewLayout(2, false));
			dropDownPlusLabel.ChildrenLayouts.Add(new ExpandToFitLayout());
			dropDownPlusLabel.X = 20;
			dropDownPlusLabel.Y = 5;
			new TextView(main.GeeUI, dropDownPlusLabel, "Actions:", Vector2.Zero, MainFont);
			this.CreateDropDownView = new DropDownView(main.GeeUI, dropDownPlusLabel, Vector2.Zero, MainFont);
			ActionsPanelView.Draggable = false;

			RootEditorView.Add(new Binding<int, Point>(RootEditorView.Width, point => point.X, main.ScreenSize));
			ComponentTabViews.Add(new Binding<int, int>(ComponentTabViews.Width, i => i / 2, RootEditorView.Width));
			ActionsPanelView.Add(new Binding<int, int>(ActionsPanelView.Width, i => i / 2, RootEditorView.Width));
			ActionsPanelView.Add(new Binding<Vector2, int>(ActionsPanelView.Position, i => new Vector2(i / 2f, 25), RootEditorView.Width));

			RootEditorView.Height.Value = 160;
			ComponentTabViews.Height.Value = 160;
			ActionsPanelView.Height.Value = 125;

			this.SelectedEntities.ItemAdded += new ListProperty<Entity>.ItemAddedEventHandler(delegate(int index, Entity item)
			{
				this.refresh();
			});
			this.SelectedEntities.ItemRemoved += new ListProperty<Entity>.ItemRemovedEventHandler(delegate(int index, Entity item)
			{
				this.refresh();
			});
			this.SelectedEntities.ItemChanged += new ListProperty<ComponentBind.Entity>.ItemChangedEventHandler(delegate(int index, Entity old, Entity newValue)
			{
				this.refresh();
			});
			this.SelectedEntities.Cleared += new ListProperty<ComponentBind.Entity>.ClearEventHandler(this.refresh);
			this.Add(new NotifyBinding(this.refresh, this.MapEditMode));

			this.PopupCommands.ItemAdded += (index, command) =>
			{
				RecomputePopupCommands();
			};
			this.PopupCommands.ItemChanged += (index, old, value) =>
			{
				RecomputePopupCommands();
			};
			this.PopupCommands.ItemRemoved += (index, command) =>
			{
				RecomputePopupCommands();
			};
		}

		private void RecomputePopupCommands()
		{
			this.CreateDropDownView.RemoveAllOptions();
			foreach (var dropDown in PopupCommands)
			{
				string text = dropDown.Description;
				if (dropDown.Chord.Key != Keys.None)
				{
					if (dropDown.Chord.Modifier != Keys.None)
						text += " [" + dropDown.Chord.Modifier.ToString() + "+" + dropDown.Chord.Key.ToString() + "]";
					else
					{
						text += " [" + dropDown.Chord.Key.ToString() + "]";
					}
				}
				CreateDropDownView.AddOption(text, () =>
				{
					dropDown.Action.Execute();
				});
			}
		}

		private Container addText(string text)
		{
			Container container = new Container();
			container.Tint.Value = Color.Black;
			container.Opacity.Value = 0.2f;
			TextElement display = new TextElement();
			display.Interpolation.Value = true;
			display.FontFile.Value = "Font";
			display.Text.Value = text;
			container.Children.Add(display);
			return container;
		}

		private void show(Entity entity)
		{
			foreach (DictionaryEntry entry in new DictionaryEntry[] { new DictionaryEntry("[" + entity.Type.ToString() + " entity]", new [] { new DictionaryEntry("ID", entity.ID) }.Concat(entity.Properties).Concat(entity.Commands)) }
				.Union(entity.Components.Where(x => ((IComponent)x.Value).Editable)))
			{
				IEnumerable<DictionaryEntry> properties = null;
				if (typeof(IComponent).IsAssignableFrom(entry.Value.GetType()))
					properties = entry.Value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
						.Select(x => new DictionaryEntry(x.Name, x.GetValue(entry.Value)))
						.Concat(entry.Value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
						.Where(y => y.GetIndexParameters().Length == 0)
						.Select(z => new DictionaryEntry(z.Name, z.GetValue(entry.Value, null))));
				else
					properties = (IEnumerable<DictionaryEntry>)entry.Value;
				properties = properties.Where(x => x.Value != null
					&& ((x.Value.GetType() == typeof(Command) && ((Command)x.Value).ShowInEditor)
					|| (typeof(IProperty).IsAssignableFrom(x.Value.GetType()) && !typeof(IListProperty).IsAssignableFrom(x.Value.GetType()) && (bool)x.Value.GetType().GetProperty("Editable").GetValue(x.Value, null))));

				if (properties.FirstOrDefault().Value == null)
					continue;


				PanelView rootEntityView = new PanelView(main.GeeUI, null, Vector2.Zero);
				rootEntityView.Add(new Binding<int, int>(rootEntityView.Width, i => i, ComponentTabViews.Width));
				rootEntityView.ChildrenLayouts.Add(new VerticalViewLayout(4, true, 6));
				this.ComponentTabViews.AddTab(entry.Key.ToString(), rootEntityView);

				Container label = this.addText(entry.Key.ToString());

				Container propertyListContainer = new Container();
				propertyListContainer.PaddingLeft.Value = 10.0f;
				propertyListContainer.PaddingRight.Value = 0.0f;
				propertyListContainer.PaddingBottom.Value = 0.0f;
				propertyListContainer.PaddingTop.Value = 0.0f;
				propertyListContainer.Opacity.Value = 0.0f;
				//this.UIElements.Add(propertyListContainer);

				ListContainer propertyList = new ListContainer();
				propertyListContainer.Children.Add(propertyList);

				label.Add(new Binding<float, bool>(label.Opacity, x => x ? 1.0f : 0.5f, label.Highlighted));

				label.Add(new CommandBinding(label.MouseLeftUp, delegate()
				{
					propertyListContainer.Visible.Value = !propertyListContainer.Visible;
				}));

				foreach (DictionaryEntry propEntry in properties)
				{
					DictionaryEntry property = propEntry;
					ListContainer row = new ListContainer();
					row.Orientation.Value = ListContainer.ListOrientation.Horizontal;

					Container keyContainer = new Container();
					keyContainer.Tint.Value = Color.Black;
					keyContainer.Opacity.Value = 0.5f;
					keyContainer.ResizeHorizontal.Value = false;
					keyContainer.Size.Value = new Vector2(128.0f, 0.0f);
					TextElement keyText = new TextElement();
					keyText.Interpolation.Value = false;
					keyText.FontFile.Value = "Font";
					keyText.Text.Value = property.Key.ToString();
					keyContainer.Children.Add(keyText);
					row.Children.Add(keyContainer);

					View containerLabel = BuildContainerLabel(property.Key.ToString());
					if (property.Value.GetType() == typeof(Command))
					{
						// It's a command
						//row.Children.Add(this.BuildButton((Command)property.Value, "[Execute]"));
						containerLabel.AddChild(BuildButton((Command)property.Value, "[Execute]"));
					}
					else
					{
						// It's a property
						containerLabel.AddChild(BuildValueView((IProperty)property.Value));
						//row.Children.Add(this.BuildValueField((IProperty)property.Value));
					}

					propertyList.Children.Add(row);
					rootEntityView.AddChild(containerLabel);
				}

				//if (typeof(IEditorGeeUIComponent).IsAssignableFrom(entry.Value.GetType()))
				//((IEditorGeeUIComponent)entry.Value).AddEditorElements(propertyList, this);
			}
		}

		public View BuildContainerLabel(string label)
		{
			var ret = new View(main.GeeUI, null);
			ret.ChildrenLayouts.Add(new HorizontalViewLayout(6));
			ret.ChildrenLayouts.Add(new ExpandToFitLayout());

			new TextView(main.GeeUI, ret, label, Vector2.Zero, MainFont);
			return ret;
		}

		public View BuildButton(Command command, string label, Color color = default(Color))
		{
			var b = new ButtonView(main.GeeUI, null, label, Vector2.Zero, MainFont);
			b.OnMouseClick += (sender, args) =>
			{
				if (command != null)
					command.Execute();
			};
			//b.ChildrenLayouts.Add(new ExpandToFitLayout());
			return b;
		}
		private void refresh()
		{
			this.ComponentTabViews.RemoveAllTabs();

			if (this.SelectedEntities.Count == 0 || this.MapEditMode)
				this.show(this.Entity);
			else if (this.SelectedEntities.Count == 1)
				this.show(this.SelectedEntities.First());
			else
				this.addText("[" + this.SelectedEntities.Count.ToString() + " entities]"); //TODO: make this do something
		}

		public void BuildValueFieldView(View parent, Type type, IProperty property, VectorElement element, int width = 30)
		{
			TextFieldView textField = new TextFieldView(main.GeeUI, parent, Vector2.Zero, MainFont);
			textField.Height.Value = 15;
			textField.Width.Value = width;
			textField.MultiLine = false;

			if (type.Equals(typeof(Vector2)))
			{
				Property<Vector2> socket = (Property<Vector2>)property;
				textField.Text = socket.Value.GetElement(element).ToString("F");
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetElement(element).ToString("F");
				}, socket));

				Action onChanged = () =>
				{
					float value;
					if (float.TryParse(textField.Text, out value))
					{
						socket.Value = socket.Value.SetElement(element, value);
					}
					textField.Text = socket.Value.GetElement(element).ToString("F");
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+(\\.\\d+)?$";
				textField.OnTextSubmitted = onChanged;
			}
			else if (type.Equals(typeof(Vector3)))
			{
				Property<Vector3> socket = (Property<Vector3>)property;
				textField.Text = socket.Value.GetElement(element).ToString("F");
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetElement(element).ToString("F");
				}, socket));

				Action onChanged = () =>
				{
					float value;
					if (float.TryParse(textField.Text, out value))
					{
						socket.Value = socket.Value.SetElement(element, value);
					}
					textField.Text = socket.Value.GetElement(element).ToString("F");
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+(\\.\\d+)?$";
				textField.OnTextSubmitted = onChanged;
			}
			else if (type.Equals(typeof(Voxel.Coord)))
			{
				Property<Voxel.Coord> socket = (Property<Voxel.Coord>)property;

				Direction dir;
				switch (element)
				{
					case VectorElement.X:
						dir = Direction.PositiveX;
						break;
					case VectorElement.Y:
						dir = Direction.PositiveY;
						break;
					default:
						dir = Direction.PositiveZ;
						break;
				}

				textField.Text = socket.Value.GetComponent(dir).ToString();
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetComponent(dir).ToString();
				}, socket));


				Action onChanged = () =>
				{
					int value;
					if (int.TryParse(textField.Text, out value))
					{
						Voxel.Coord c = socket.Value;
						c.SetComponent(dir, value);
						socket.Value = c;
					}
					textField.Text = socket.Value.GetComponent(dir).ToString();
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+$";
				textField.OnTextSubmitted = onChanged;
			}
			else if (type.Equals(typeof(Vector4)))
			{
				Property<Vector4> socket = (Property<Vector4>)property;
				textField.Text = socket.Value.GetElement(element).ToString("F");
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetElement(element).ToString("F");
				}, socket));

				Action onChanged = () =>
				{
					float value;
					if (float.TryParse(textField.Text, out value))
					{
						socket.Value = socket.Value.SetElement(element, value);
					}
					textField.Text = socket.Value.GetElement(element).ToString("F");
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+(\\.\\d+)?$";
				textField.OnTextSubmitted = onChanged;
			}
			else if (type.Equals(typeof(Quaternion)))
			{
				Property<Quaternion> socket = (Property<Quaternion>)property;
				textField.Text = socket.Value.GetElement(element).ToString("F");
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetElement(element).ToString("F");
				}, socket));

				Action onChanged = () =>
				{
					float value;
					if (float.TryParse(textField.Text, out value))
					{
						socket.Value = socket.Value.SetElement(element, value);
					}
					textField.Text = socket.Value.GetElement(element).ToString("F");
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+(\\.\\d+)?$";
				textField.OnTextSubmitted = onChanged;
			}
			else if (type.Equals(typeof(Color)))
			{
				Property<Color> socket = (Property<Color>)property;
				textField.Text = socket.Value.GetElement(element).ToString("F");
				socket.AddBinding(new NotifyBinding(() =>
				{
					textField.Text = socket.Value.GetElement(element).ToString("F");
				}, socket));

				Action onChanged = () =>
				{
					byte value;
					if (byte.TryParse(textField.Text, out value))
					{
						socket.Value = socket.Value.SetElement(element, value);
					}
					textField.Text = socket.Value.GetElement(element).ToString("F");
					textField.Selected.Value = false;
				};
				textField.ValidationRegex = "^\\d+$";
				textField.OnTextSubmitted = onChanged;
			}
		}

		public View BuildValueView(IProperty property)
		{
			View ret = new View(main.GeeUI, null);
			ret.ChildrenLayouts.Add(new HorizontalViewLayout(4));
			ret.ChildrenLayouts.Add(new ExpandToFitLayout());

			PropertyInfo propertyInfo = property.GetType().GetProperty("Value");
			if (propertyInfo.PropertyType.Equals(typeof(Vector2)))
			{
				foreach (VectorElement field in new[] { VectorElement.X, VectorElement.Y })
					this.BuildValueFieldView(ret, propertyInfo.PropertyType, property, field);
			}
			else if (propertyInfo.PropertyType.Equals(typeof(Vector3)) || propertyInfo.PropertyType.Equals(typeof(Voxel.Coord)))
			{
				foreach (VectorElement field in new[] { VectorElement.X, VectorElement.Y, VectorElement.Z })
					this.BuildValueFieldView(ret, propertyInfo.PropertyType, property, field);
			}
			else if (propertyInfo.PropertyType.Equals(typeof(Vector4)) || propertyInfo.PropertyType.Equals(typeof(Quaternion)) ||
					 propertyInfo.PropertyType.Equals(typeof(Color)))
			{
				foreach (VectorElement field in new[] { VectorElement.X, VectorElement.Y, VectorElement.Z, VectorElement.W })
					this.BuildValueFieldView(ret, propertyInfo.PropertyType, property, field);
			}
			else if (typeof(Enum).IsAssignableFrom(propertyInfo.PropertyType))
			{
				var fields = propertyInfo.PropertyType.GetFields(BindingFlags.Static | BindingFlags.Public);
				int numFields = fields.Length;
				var drop = new DropDownView(main.GeeUI, ret, Vector2.Zero, MainFont);
				for (int i = 0; i < numFields; i++)
				{
					int i1 = i;
					Action onClick = () =>
					{
						propertyInfo.SetValue(property, Enum.ToObject(propertyInfo.PropertyType, i1), null);
					};
					drop.AddOption(fields[i].Name, onClick);
				}
				drop.SetSelectedOption(propertyInfo.GetValue(property, null).ToString(), false);
				drop.Add(new NotifyBinding(() =>
				{
					drop.SetSelectedOption(propertyInfo.GetValue(property, null).ToString(), false);
				}, (IProperty)property));
			}
			else
			{
				TextFieldView view = new TextFieldView(main.GeeUI, ret, Vector2.Zero, MainFont);
				view.Width.Value = 70;
				view.Height.Value = 15;
				view.Text = "abc";
				view.MultiLine = false;

				if (propertyInfo.PropertyType.Equals(typeof(int)))
				{
					Property<int> socket = (Property<int>)property;
					view.Text = socket.Value.ToString();
					socket.AddBinding(new NotifyBinding(() =>
					{
						view.Text = socket.Value.ToString();
					}, socket));
					Action onChanged = () =>
					{
						int value;
						if (int.TryParse(view.Text, out value))
						{
							socket.Value = value;
						}
						view.Text = socket.Value.ToString();
						view.Selected.Value = false;
					};
					view.ValidationRegex = "^\\d+$";
					view.OnTextSubmitted = onChanged;
				}
				else if (propertyInfo.PropertyType.Equals(typeof(float)))
				{
					Property<float> socket = (Property<float>)property;
					view.Text = socket.Value.ToString("F");
					socket.AddBinding(new NotifyBinding(() =>
					{
						view.Text = socket.Value.ToString();
					}, socket));
					Action onChanged = () =>
					{
						float value;
						if (float.TryParse(view.Text, out value))
						{
							socket.Value = value;
						}
						view.Text = socket.Value.ToString("F");
						view.Selected.Value = false;
					};
					view.ValidationRegex = "^\\d+(\\.\\d+)?$";
					view.OnTextSubmitted = onChanged;
				}
				else if (propertyInfo.PropertyType.Equals(typeof(bool)))
				{
					//No need for a textfield!
					ret.RemoveChild(view);
					CheckBoxView checkBox = new CheckBoxView(main.GeeUI, ret, Vector2.Zero, "", MainFont);
					Property<bool> socket = (Property<bool>)property;
					checkBox.IsChecked.Value = socket.Value;
					checkBox.Add(new NotifyBinding(() =>
					{
						this.NeedsSave.Value = true;
						socket.Value = checkBox.IsChecked.Value;
					}, checkBox.IsChecked));
				}
				else if (propertyInfo.PropertyType.Equals(typeof(string)))
				{
					Property<string> socket = (Property<string>)property;

					if (socket.Value == null) view.Text = "";
					else view.Text = socket.Value;

					socket.AddBinding(new NotifyBinding(() =>
					{
						var text = socket.Value;
						if (text == null) text = "";
						view.Text = text;
					}, socket));

					//Vast majority of strings won't be multiline.
					if (socket.Value != null)
						view.MultiLine = socket.Value.Contains("\n");
					Action onChanged = () =>
					{
						if (socket.Value != view.Text)
							socket.Value = view.Text;
						view.Selected.Value = false;
					};
					view.OnTextSubmitted = onChanged;
				}
			}
			return ret;
		}

		public override void delete()
		{
			base.delete();
			RootEditorView.ParentView.RemoveChild(RootEditorView);
		}
	}
}
