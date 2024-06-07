import flet as ft

from logic.agent import select, session, Settings 
from frontend.components.gui_constants import *


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
    self.page.locale_configuration.current_locale = ft.Locale(self.lang, self.loc)
    self.translation.load_lang(self.lang)
    self.content.controls[0].value = 'updated'
    # Insert language and locale setting into the database
    locale = session.scalars(select(Settings).filter_by(key='locale')).first()
    if locale:
      locale.value = {'lang': self.lang, 'loc': self.loc}
    else:
      session.add(Settings(key='locale', value={'lang': self.lang, 'loc': self.loc}))
    session.commit()
    self.page.update()


class PopupColorItem(ft.PopupMenuItem):
  def __init__(self, colour, name):
    super().__init__()
    self.content = ft.Row(
      controls=[
        ft.Icon(
          name=ft.icons.COLOR_LENS_OUTLINED, color=colour,
        ),
      ],
    )
    self.on_click = self.seed_color_changed
    self.data = colour

  def seed_color_changed(self, e):
    self.page.theme = self.page.dark_theme = ft.theme.Theme(
      color_scheme_seed=self.data
    )
    self.page.update()


class NavigationItem(ft.Container):
  def __init__(self, destination, item_clicked):
    super().__init__()
    self.ink = True
    self.padding = 5
    self.border_radius = 5
    self.destination = destination
    self.icon = destination.icon
    self.text = destination.label
    self.content = ft.Row([ft.Icon(self.icon), ft.Text(self.text)])
    self.on_click = item_clicked


class SideBarColumn(ft.Column):
  def __init__(self, chat):
    super().__init__()
    self.gallery = chat
    # self.expand = 4
    self.spacing = 0
    self.scroll = ft.ScrollMode.ALWAYS
    self.width = 40
    self.selected_index = 0
    self.controls = self.get_navigation_items()

  def before_update(self):
    super().before_update()
    self.update_selected_item()

  def get_navigation_items(self):
    navigation_items = []
    for destination in self.gallery.control_groups:
      navigation_items.append(
          NavigationItem(destination, item_clicked=self.item_clicked)
      )
    return navigation_items

  def item_clicked(self, e):
    self.selected_index = e.control.destination.index
    self.update_selected_item()
    self.page.go(f"/{e.control.destination.name}")

  def update_selected_item(self):
    self.selected_index = self.gallery.control_groups.index(
        self.gallery.selected_control_group
    )
    for item in self.controls:
        item.bgcolor = None
        item.content.controls[0].name = item.destination.icon
    self.controls[self.selected_index].bgcolor = ft.colors.SECONDARY_CONTAINER
    self.controls[self.selected_index].content.controls[0].name = self.controls[
        self.selected_index
    ].destination.selected_icon


class Sidebar(ft.Column):
  def __init__(self, page, chat, lang, context_list, mainwindow):
    super().__init__()
    self.chat = chat
    self.l = lang
    self.p = page
    self.spacing = 0
    self.padding = 0
    self.width = 47
    self.spacing = 0
    self.alignment = "end"
    self.context_list = context_list
    self.mainwindow = mainwindow

    self._button_padding = ft.padding.only(4, 4, 3, 0)

    self.dark_light_icon =  ft.IconButton(
      icon=ft.icons.BRIGHTNESS_HIGH,
      # tooltip="Toggle dark/light",
      on_click=self.theme_changed, padding=0,
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)))
    
    self.colour_theme_menu = ft.PopupMenuButton(
      icon=ft.icons.COLOR_LENS_OUTLINED,
      shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=60,
      height=60,
      left=-14,
      top=-13,
      
      # tooltip="Theme",
      items=[
        PopupColorItem(colour="deeppurple", name="Deep purple"),
        PopupColorItem(colour="indigo", name="Indigo"),
        PopupColorItem(colour="blue", name="Blue (default)"),
        PopupColorItem(colour="teal", name="Teal"),
        PopupColorItem(colour="green", name="Green"),
        PopupColorItem(colour="yellow", name="Yellow"),
        PopupColorItem(colour="orange", name="Orange"),
        PopupColorItem(colour="deeporange", name="Deep orange"),
        PopupColorItem(colour="pink", name="Pink"),
        PopupColorItem(colour="red", name="Red"),
        PopupColorItem(colour="#202020", name="Dark"),
        PopupColorItem(colour="#fefefe", name="Light")
      ],
    )

    self.long_colour_theme_menu = ft.PopupMenuButton(
      icon=ft.icons.COLOR_LENS_OUTLINED,
      # shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=400,
      height=60,
      left=-180,
      top=-9,
      
      # tooltip="Theme",
      items=[
        PopupColorItem(colour="deeppurple", name="Deep purple"),
        PopupColorItem(colour="indigo", name="Indigo"),
        PopupColorItem(colour="blue", name="Blue (default)"),
        PopupColorItem(colour="teal", name="Teal"),
        PopupColorItem(colour="green", name="Green"),
        PopupColorItem(colour="yellow", name="Yellow"),
        PopupColorItem(colour="orange", name="Orange"),
        PopupColorItem(colour="deeporange", name="Deep orange"),
        PopupColorItem(colour="pink", name="Pink"),
        PopupColorItem(colour="red", name="Red"),
        PopupColorItem(colour="#202020", name="Dark"),
        PopupColorItem(colour="#fefefe", name="Light")
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
        PopupLanguageItem(translation=self.l, lang="es"),
        PopupLanguageItem(translation=self.l, lang="zh"),
      ],
    )

    self.long_language_menu = ft.PopupMenuButton(
      icon=ft.icons.TRANSLATE,
      width=400,
      height=60,
      left=-180,
      top=-9,
      
      items=[
        PopupLanguageItem(translation=self.l, lang="en"),
        PopupLanguageItem(translation=self.l, lang="es"),
        PopupLanguageItem(translation=self.l, lang="zh"),
      ],
    )

    def toggler(_):
      chat_window.visible = not chat_window.visible
      chat_window.update()
    
    self.controls = [
      # App drawer
      ft.Container(
        content=ft.IconButton(ft.icons.MENU, on_click=self.toggler, padding=0, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))),
        padding=ft.padding.only(4, 0, 3, 0)
      ),

      # Sidebar column
      ft.Column([
        # Default chat workspace
        ft.Container(content=ft.IconButton(ft.icons.CHAT_OUTLINED,
        on_click=self.show_threads, padding=0, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))), padding=self._button_padding),

      ], expand=4, spacing=0),

      # Horizontal divider
      ft.Container(
        content=ft.Divider(height=1, color=chat_bg_colour),
        padding=ft.padding.only(6, 0, 5, 0)
      ),

      # Show settings
      ft.Container(content=ft.IconButton(ft.icons.SETTINGS, on_click=self.show_settings, padding=0, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))), padding=self._button_padding),

      # Dark/Light theme toggle
      ft.Container(content=
        self.dark_light_icon,
        padding=ft.padding.only(4, 4, 3, 0)
      ),

      # Language menu
      ft.Container(
        content=ft.Row([
          ft.Container(
            content=ft.Stack([
              self.language_menu
            ], clip_behavior=ft.ClipBehavior.HARD_EDGE),
            padding=ft.padding.only(0,0,0,0),
            margin=ft.margin.only(4,4,3,0),
            shape=ft.RoundedRectangleBorder(radius=3),
          )
        ], height=40, width=40, spacing=0),
        padding=ft.padding.only(0,0,0,0),
        margin=ft.margin.only(4,4,3,0),
        border_radius=ft.BorderRadius(3,3,3,3),
        clip_behavior=ft.ClipBehavior.HARD_EDGE,
      ),

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
        margin=ft.margin.only(4,4,3,4),
        border_radius=ft.BorderRadius(3,3,3,3),
        clip_behavior=ft.ClipBehavior.HARD_EDGE,
      ), 
    ]

    # Load app drawer
    self.p.drawer = ft.NavigationDrawer(
      controls=[
        ft.Container(content=
          ft.Row(
            [
              ft.Container(content=
                ft.Column([
                    ft.Container(
                      ft.Text("Subconscious"),
                      padding=ft.padding.only(10, 10, 10, 10),
                      height=40
                    ),

                    # App drawer
                    ft.Container(
                      content=ft.TextButton(
                        content=ft.Row([ft.Icon(ft.icons.MENU), ft.Text(self.l.sidebar.menu)]),
                        on_click=self.toggler, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3), padding=ft.padding.only(8, 0, 0, 0)), expand=True, width=193, height=40),
                      padding=ft.padding.only(4, 0, 3, 4)
                    ),

                    ft.Column([
                      # Main worksapce
                      ft.Container(
                        content=ft.TextButton(
                          content=ft.Row([ft.Icon(ft.icons.CHAT_OUTLINED), ft.Text(self.l.sidebar.home)]),
                          on_click=self.show_threads, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3), padding=ft.padding.only(8, 0, 0, 0)), expand=True, width=193, height=40),
                        padding=ft.padding.only(4, 0, 3, 4)
                      ),

                    ], spacing=0, expand=4),

                    # Horizontal divider
                    ft.Container(
                      content=ft.Divider(height=1, color=chat_bg_colour),
                      padding=ft.padding.only(6, 0, 5, 0)
                    ),

                    # Settings
                    ft.Container(
                      content=ft.TextButton(
                        content=ft.Row([ft.Icon(ft.icons.SETTINGS), ft.Text(self.l.sidebar.settings)]),
                        on_click=self.show_settings, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3), padding=ft.padding.only(8, 0, 0, 0)), expand=True, width=193, height=40),
                      padding=ft.padding.only(4, 4, 3, 0)
                    ),

                    ft.Container(
                      content=ft.TextButton(
                        content=ft.Row([ft.Icon(ft.icons.BRIGHTNESS_HIGH), ft.Text(self.l.sidebar.toggle)]),
                        on_click=self.theme_changed, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3), padding=ft.padding.only(8, 0, 0, 0)), expand=True, width=193, height=40),
                      padding=ft.padding.only(4, 4, 3, 0)
                    ),

                    # Language menu
                    ft.Container(
                      content=ft.Row([
                        ft.Container(
                          content=ft.Stack([
                            ft.Text(
                              self.l.sidebar.lang,
                              height=50, width=200, top=10, left=42,
                              weight=ft.FontWeight.W_600, color=ft.colors.PRIMARY,
                            ),
                            self.long_language_menu,
                          ]),
                          shape=ft.RoundedRectangleBorder(radius=3),
                        )
                      ], height=40, width=193, spacing=0),
                      margin=ft.margin.only(4,4,3,0),
                      border_radius=ft.BorderRadius(3,3,3,3),
                    ),

                    # Language menu
                    ft.Container(
                      content=ft.Row([
                        ft.Container(
                          content=ft.Stack([
                            ft.Text(
                              self.l.sidebar.theme, 
                              height=50, width=200, top=10, left=42,
                              weight=ft.FontWeight.W_600, color=ft.colors.PRIMARY,
                            ),
                            self.long_colour_theme_menu,
                          ]),
                          shape=ft.RoundedRectangleBorder(radius=3),
                        )
                      ], height=40, width=193, spacing=0),
                      margin=ft.margin.only(4,4,3,2),
                      border_radius=ft.BorderRadius(3,3,3,3),
                    )
                  ],
                  expand=True,
                  spacing=0,
                  scroll=None,
                ),
                border=ft.border.only(right=ft.BorderSide(1, outline_colour), bottom=ft.BorderSide(1, outline_colour)),
                bgcolor=app_bg_colour, 
                border_radius=ft.BorderRadius(0, 7, 0, 7), 
                width=200, # Sets the container width only inside of a column
              ),

              ft.Container(content=
                ft.Column([], scroll=None),
                on_click=self.toggler, expand=True
              )
            ],
            spacing=0, width=250, scroll=None # No effect on the column width
          ),
          height=self.starting_height(),
        )
      ],
      bgcolor=ft.colors.TRANSPARENT,
      shadow_color=ft.colors.TRANSPARENT,
      surface_tint_color=ft.colors.TRANSPARENT,
    )

    self.p.on_window_event = self.dynamic_height

  def dynamic_height(self, e):
    """ Set the height of the chat window """
    if self.p.window_maximized:
      self.p.drawer.controls[0].height = self.p.window_height-15
    else:
      self.p.drawer.controls[0].height = self.p.window_height-8
    self.p.drawer.update()

  def starting_height(self):
    """ Set the height of the chat window """
    if self.p.window_maximized:
      return self.p.window_height-15
    else:
      return self.p.window_height-8
  
  def toggler(self, _):
    """ Toggle the app drawer """
    self.p.drawer.open = not self.p.drawer.open
    self.p.drawer.controls[0].height = self.p.window_height-8
    self.p.drawer.update()

  def theme_changed(self, _):
    """ Toggle the theme mode between light and dark """
    if self.page.theme_mode == ft.ThemeMode.LIGHT:
      self.page.theme_mode = ft.ThemeMode.DARK
      self.dark_light_icon.icon = ft.icons.BRIGHTNESS_HIGH
    elif self.page.theme_mode == ft.ThemeMode.DARK:
      self.page.theme_mode = ft.ThemeMode.LIGHT
      self.dark_light_icon.icon = ft.icons.BRIGHTNESS_2
    self.page.update()
  
  def toggler(self, e):
    self.p.drawer.open = not self.p.drawer.open
    self.p.drawer.update()
  
  def show_settings(self, e):
    self.context_list.show_settings(e)
    self.page.update()
  
  def show_threads(self, e):
    self.context_list.show_threads(e)
    self.page.update()
  