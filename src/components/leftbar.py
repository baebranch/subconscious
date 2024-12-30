import json
import flet as ft
from src.utilities.filechange import FileChange


class PopupLanguageItem(ft.PopupMenuItem):
  def __init__(self, translation, lang, loc=None):
    super().__init__()
    self.translation = translation
    self.lang = lang
    self.loc = loc
    self.content = ft.Row(
      controls=[
        ft.Text(lang),
      ],
    )
    self.on_click = self.language_changed
    self.language = lang

  def language_changed(self, e):
    # TODO: Update to use settings json file
    self.page.locale_configuration.current_locale = ft.Locale(self.lang, self.loc)
    self.translation.load_lang(self.lang)
    # self.content.controls[0].value = 'updated'
    # # Insert language and locale setting into the database
    # locale = session.scalars(select(Settings).filter_by(key='locale')).first()
    # if locale:
    #   locale.value = {'lang': self.lang, 'loc': self.loc}
    # else:
    #   session.add(Settings(key='locale', value={'lang': self.lang, 'loc': self.loc}))
    # session.commit()
    # self.page.update()
    pass


class PopupColorItem(ft.PopupMenuItem):
  white = ft.ColorScheme(primary=ft.colors.WHITE, secondary=ft.colors.GREY, background=ft.colors.BLACK87, secondary_container=ft.colors.GREY_800, primary_container=ft.colors.GREY_800)
  black = ft.ColorScheme(primary=ft.colors.BLACK, secondary=ft.colors.GREY, background=ft.colors.WHITE, secondary_container=ft.colors.GREY_300, primary_container=ft.colors.GREY_300)

  def __init__(self, colour, name, settings):
    super().__init__()
    self.settings = settings
    self.content = ft.Row(
      controls=[
        ft.Icon(
          name=ft.icons.COLOR_LENS_OUTLINED, color=colour,
        ),
        ft.Text(name),
      ],
    )
    self.on_click = self.seed_color_changed
    self.data = colour

  def seed_color_changed(self, e):
    # Update settings
    def update_settings():
      self.settings['_general']['theme'] = self.data
      return self.settings
    FileChange(update_settings)

    # Set the theme to the selected colour
    if self.data in ["black", "white"]:
      self.page.theme = ft.Theme(color_scheme=getattr(self, self.data))
    else:
      self.page.theme = ft.Theme(color_scheme_seed=self.data)
    self.page.update()


class WorkspaceIcon(ft.Container):
  def __init__(self, workspace, show_threads):
    super().__init__()
    self.workspace = workspace
    self.content=ft.IconButton(
      ft.icons.CHAT_OUTLINED,
      on_click=lambda _: show_threads(self.workspace['id']),
      padding=0,
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
      tooltip=self.workspace['name']
    )
    self.padding = ft.padding.only(4, 0, 4, 4)


class WorksapceButton(ft.Container):
  def __init__(self, workspace, show_threads, title):
    super().__init__()
    self.workspace = workspace
    self.content=ft.TextButton(
      content=ft.Row([
        ft.Icon(ft.icons.CHAT_OUTLINED),
        ft.Text(title)
      ]),
      on_click=lambda _: show_threads(self.workspace.id), 
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3),
      padding=ft.padding.only(8, 0, 0, 0)),
      expand=True,
      width=193,
      height=40
    )
    self.padding = ft.padding.only(4, 0, 3, 4)


class Leftbar(ft.Column):
  def __init__(self, page, lang, context_list, mainwindow, titlebar_theme_changed, settings):
    super().__init__()
    self.l = lang
    self.p = page
    self.padding = 0
    self.width = 48
    self.spacing = 0
    self.alignment = "end"
    self.settings = settings
    self.mainwindow = mainwindow
    self.context_list = context_list
    self.titlebar_toggle = titlebar_theme_changed

    # Theme colours
    self.black_item = PopupColorItem(colour="black", name="Black", settings=self.settings)
    self.white_item = PopupColorItem(colour="white", name="White", settings=self.settings)

    self._button_padding = ft.padding.only(4, 0, 4, 4)

    self.dark_light_icon =  ft.IconButton(
      icon=ft.icons.BRIGHTNESS_HIGH,
      tooltip="Toggle dark/light mode",
      on_click=self.theme_changed, padding=0,
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)))
    
    self.colour_theme_menu = ft.PopupMenuButton(
      icon=ft.icons.COLOR_LENS_OUTLINED,
      shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=60,
      height=60,
      left=-14,
      top=-13,
      
      tooltip="Theme",
      items=[
        PopupColorItem(colour="deeppurple", name="Deep purple", settings=self.settings),
        PopupColorItem(colour="indigo", name="Indigo", settings=self.settings),
        PopupColorItem(colour="blue", name="Blue (default)", settings=self.settings),
        PopupColorItem(colour="teal", name="Teal", settings=self.settings),
        PopupColorItem(colour="green", name="Green", settings=self.settings),
        PopupColorItem(colour="yellow", name="Yellow", settings=self.settings),
        PopupColorItem(colour="orange", name="Orange", settings=self.settings),
        PopupColorItem(colour="deeporange", name="Deep orange", settings=self.settings),
        PopupColorItem(colour="pink", name="Pink", settings=self.settings),
        PopupColorItem(colour="red", name="Red", settings=self.settings),
        self.black_item if self.settings._general.mode == ft.ThemeMode.LIGHT.value else self.white_item
      ],
    )

    self.language_menu = ft.PopupMenuButton(
      icon=ft.icons.TRANSLATE,
      shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=60,
      height=60,
      left=-14,
      top=-13,
      
      items=[
        PopupLanguageItem(translation=self.l, lang="en"),
        # PopupLanguageItem(translation=self.l, lang="es"),
        # PopupLanguageItem(translation=self.l, lang="zh"),
      ],
    )

    def toggler(_):
      chat_window.visible = not chat_window.visible
      chat_window.update()
    
    self.workspaces = ft.Column([
      ], expand=4, spacing=0)

    # Add the workspace icons to the sidebar
    # for workspace in self.chat.workspaces:
    self.workspaces.controls.append(
      WorkspaceIcon({"name": "Chat", "id": 1}, self.show_threads)
    )

    self.controls = [
      # Sidebar column
      self.workspaces,

      # Horizontal divider
      # ft.Container(
      #   content=ft.Divider(height=1, color=ft.colors.PRIMARY),
      #   padding=ft.padding.only(6, 4, 5, 4)
      # ),

      # Show settings
      ft.Container(content=ft.IconButton(ft.icons.SETTINGS, on_click=self.show_settings, padding=0, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)), tooltip="Settings"), padding=self._button_padding),

      # Dark/Light theme toggle
      ft.Container(content=
        self.dark_light_icon,
        padding=self._button_padding
        # padding=ft.padding.only(4, 4, 4, 0)
      ),

      # Language menu
      # ft.Container(
      #   content=ft.Row([
      #     ft.Container(
      #       content=ft.Stack([
      #         self.language_menu
      #       ], clip_behavior=ft.ClipBehavior.HARD_EDGE),
      #       padding=ft.padding.only(0,0,0,0),
      #       margin=ft.margin.only(4,4,3,0),
      #       shape=ft.RoundedRectangleBorder(radius=3),
      #     )
      #   ], height=40, width=40, spacing=0),
      #   padding=ft.padding.only(0,0,0,0),
      #   margin=ft.margin.only(4,4,3,0),
      #   border_radius=ft.BorderRadius(3,3,3,3),
      #   clip_behavior=ft.ClipBehavior.HARD_EDGE,
      # ),

      # Colour theme menu
      ft.Container(
        content=ft.Row([
          ft.Container(
            content=ft.Stack([
              self.colour_theme_menu
            ], clip_behavior=ft.ClipBehavior.HARD_EDGE),
            padding=ft.padding.only(0,0,0,0),
            margin=ft.margin.only(4,4,3,4),
            shape=ft.RoundedRectangleBorder(radius=3),
          )
        ], height=40, width=40, spacing=0),
        padding=ft.padding.only(0,0,0,0),
        # padding=self._button_padding,
        margin=ft.margin.only(4,0,4,4),
        border_radius=ft.BorderRadius(3,3,3,3),
        clip_behavior=ft.ClipBehavior.HARD_EDGE,
      ), 
    ]

  def dynamic_height(self, e):
    """ Set the height of the chat window """
    if self.page.drawer and self.page.drawer.open:
      if self.page.window.maximized:
        self.p.drawer.controls[0].height = self.page.window.height-15
      else:
        self.page.drawer.controls[0].height = self.page.window.height-8
      self.page.drawer.update()

  # def starting_height(self):
  #   """ Set the height of the chat window """
  #   if self.p.Window.maximized:      
  #     return self.p.Window.height -15
  #   else:
  #     return self.p.Window.height-8
  
  def toggler(self, _):
    """ Toggle the app drawer """
    self.page.open(self.drawer)
    if self.page.window.maximized:
      self.page.drawer.controls[0].height = self.page.window.height-16
    else:
      self.page.drawer.controls[0].height = self.page.window.height-9
    self.page.drawer.update()

  def theme_changed(self, _):
    """ Toggle the theme mode between light and dark """
    if self.page.theme_mode == ft.ThemeMode.LIGHT:
      self.minimize = ft.Image(src="./src/assets/minimize_light.svg", width=12, height=12)
      self.maximize = ft.Image(src="./src/assets/maximize_light.svg", width=12, height=12)
      self.restore = ft.Image(src="./src/assets/restore_light.svg", width=12, height=12)
      self.close = ft.Image(src="./src/assets/close_light.svg", width=12, height=12)
      self.dark_light_icon.icon = ft.icons.BRIGHTNESS_HIGH
      self.page.theme_mode = ft.ThemeMode.DARK
      self.colour_theme_menu.items.pop(-1)
      self.colour_theme_menu.items.append(self.white_item)

      # Update theme if it is white
      if self.page.theme.color_scheme and self.page.theme.color_scheme.primary == "black":
        self.page.theme = ft.Theme(color_scheme=PopupColorItem.white)

    elif self.page.theme_mode == ft.ThemeMode.DARK:
      self.minimize = ft.Image(src="./src/assets/minimize_dark.svg", width=12, height=12)
      self.maximize = ft.Image(src="./src/assets/maximize_dark.svg", width=12, height=12)
      self.restore = ft.Image(src="./src/assets/restore_dark.svg", width=12, height=12)
      self.close = ft.Image(src="./src/assets/close_dark.svg", width=12, height=12)
      self.dark_light_icon.icon = ft.icons.BRIGHTNESS_2
      self.page.theme_mode = ft.ThemeMode.LIGHT
      self.colour_theme_menu.items.pop(-1)
      self.colour_theme_menu.items.append(self.black_item)

      # Update theme if it is white
      if self.page.theme.color_scheme and self.page.theme.color_scheme.primary == "white":
        self.page.theme = ft.Theme(color_scheme=PopupColorItem.black)

    # Update the theme of the title bar and the page
    self.titlebar_toggle()
    self.page.update()

    # Update the settings
    def update_theme():
      self.settings['_general']['mode'] = self.page.theme_mode.value
      if self.settings['_general']['theme'] in ["black", "white"]:
        self.settings['_general']['theme'] = "black" if self.page.theme_mode == ft.ThemeMode.LIGHT else "white"
      return self.settings

    FileChange(update_theme)
  
  def show_settings(self, e):
    self.context_list.show_settings(e)
    self.page.update()
  
  def show_threads(self, e):
    self.context_list.show_threads(e)
    # self.page.update()
  
  # def workspace_icon(self, workspace):
  #   """ Creates a worksapce icon button class """
  #   class WorkspaceIcon(ft.Container):
  #     def __init__(this, workspace):
  #       this.workspace = workspace
  #       this.content=ft.IconButton(
  #         ft.icons.CHAT_OUTLINED,
  #         on_click=lambda _: self.show_threads(this.workspace.id),
  #         padding=0,
  #         style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))
  #       ),
  #       this.padding=self._button_padding
    
  #   return WorkspaceIcon(workspace)
  
  # def workspace_button(self, WORKSPACE):
  # # def workspace_button(self, workspace):
  #   """ Creates a workspace text button class """
  #   global WORKSPACE
  #   # home = self.l.sidebar.home
  #   # show_threads = self.show_threads
  #   class WorkspaceButton(ft.Container):
  #     workspace = workspace
  #     content=ft.TextButton(
  #       content=ft.Row([
  #         ft.Icon(ft.icons.CHAT_OUTLINED),
  #         ft.Text(self.l.sidebar.home)
  #       ]),
  #       on_click=lambda _: self.show_threads(workspace.id), 
  #       style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3),
  #       padding=ft.padding.only(8, 0, 0, 0)),
  #       expand=True,
  #       width=193,
  #       height=40
  #     ),
  #     padding = ft.padding.only(4, 0, 3, 4)
  #   class WorkspaceButton(ft.Container):
  #     def __init__(self, workspace):
  #       self.workspace = workspace
  #       self.content=ft.TextButton(
  #         content=ft.Row([
  #           ft.Icon(ft.icons.CHAT_OUTLINED),
  #           ft.Text(home)
  #         ]),
  #         on_click=lambda _: show_threads(self.workspace.id), 
  #         style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3),
  #         padding=ft.padding.only(8, 0, 0, 0)),
  #         expand=True,
  #         width=193,
  #         height=40
  #       ),
  #       self.padding = ft.padding.only(4, 0, 3, 4)
    
  #   return WorkspaceButton()
  