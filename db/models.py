from db import *


class Workspace(Base):
  """ Workspaces table to track groups of chat sessions """
  __tablename__ = 'workspaces'

  id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
  name: Mapped[str] = mapped_column(String(255), nullable=False)
  description: Mapped[str] = mapped_column(String(255), nullable=True)
  icon: Mapped[str] = mapped_column(String(255), nullable=True)
  meta: Mapped[dict] = mapped_column(JSON, nullable=True)
  updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now, onupdate=datetime.now)
  created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now)

  chats: Mapped['Chat'] = relationship('Chat', back_populates='workspace')

  @validates('name')
  def validate_name(self, key, name):
    if not name:
      raise ValueError('Name is required')
    return name
  
  def __repr__(self):
    return f'Workspace(id={self.id!r}, name={self.name!r}, description={self.description!r}, icon={self.icon!r}, meta={self.meta!r}, updated_at={self.updated_at!r}, created_at={self.created_at!r})'
  
  def to_dict(self):
    return {
      'id': self.id,
      'name': self.name,
      'description': self.description,
      'icon': self.icon,
      'meta': self.meta,
      'updated_at': self.updated_at,
      'created_at': self.created_at
    }


class Chat(Base):
  """ Chat table to track chat sessions """
  __tablename__ = 'chats'

  id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
  workspace_id: Mapped[int] = mapped_column(Integer, ForeignKey('workspaces.id'), nullable=False)
  title: Mapped[str] = mapped_column(String(255), nullable=False)
  description: Mapped[str] = mapped_column(String(255), nullable=True)
  prompt: Mapped[str] = mapped_column(Text, nullable=True)  
  meta: Mapped[dict] = mapped_column(JSON, nullable=True)
  updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now, onupdate=datetime.now)
  created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now)

  workspace: Mapped['Workspace'] = relationship('Workspace', back_populates='chats')

  @validates('title')
  def validate_title(self, key, title):
    if not title:
      raise ValueError('Title is required')
    return title
  
  def __repr__(self):
    return f'Chat(id={self.id!r}, workspace_id={self.workspace_id!r}, title={self.title!r}, description={self.description!r}, prompt={self.prompt!r}, meta={self.meta!r}, updated_at={self.updated_at!r}, created_at={self.created_at!r})'
  
  def to_dict(self):
    return {
      'id': self.id,
      'workspace_id': self.workspace_id,
      'title': self.title,
      'description': self.description,
      'prompt': self.prompt,
      'meta': self.meta,
      'updated_at': self.updated_at,
      'created_at': self.created_at
    }


class Settings(Base):
  """ Stores the user settings """
  __tablename__ = 'settings'

  id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
  key: Mapped[str] = mapped_column(String(255), nullable=False)
  value: Mapped[dict] = mapped_column(JSON, nullable=True)
  meta: Mapped[dict] = mapped_column(JSON, nullable=True)
  updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now, onupdate=datetime.now)
  created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=datetime.now)

  def __repr__(self):
    return f'Settings(id={self.id!r}, key={self.key!r}, value={self.value!r}, meta={self.meta!r}, updated_at={self.updated_at!r}, created_at={self.created_at!r})'
  
  def to_dict(self):
    return {
      'id': self.id,
      'key': self.key,
      'value': self.value,
      'meta': self.meta,
      'updated_at': self.updated_at,
      'created_at': self.created_at
    }

# Create tables
Base.metadata.create_all(engine)

# Create session
Session = sessionmaker(bind=engine)
session = Session()

# Create the default Workspace, Chat and settings
default_workspace = session.scalars(select(Workspace).filter_by(id=1)).first()
if not default_workspace:
  default_workspace = Workspace(**{
    'name': 'Chat',
    'description': 'The default workspace for Subconscious',
    'updated_at': datetime.now(timezone.utc),
    'created_at': datetime.now(timezone.utc)
  })
  session.add(default_workspace)
  session.commit()

default_chat = session.scalars(select(Chat).filter_by(id=1)).first()
if not default_chat:
  default_chat = Chat(**{
    'workspace_id': default_workspace.id,
    'title': 'Home',
    'description': 'The main chat for general AI interaction',
    'prompt': 'You are a helpful assistant. Answer all questions to the best of your ability.',
    'updated_at': datetime.now(timezone.utc),
    'created_at': datetime.now(timezone.utc)
  })
  session.add(default_chat)
  session.commit()

tray_icon = session.scalars(select(Settings).filter_by(key='tray_icon')).first()
if not tray_icon:
  tray_icon = Settings(**{
    'key': 'tray_icon',
    'value': {'show': True}
  })
  session.add(tray_icon)
  session.commit()
