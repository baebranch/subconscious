import sqlalchemy as sql
from datetime import datetime, timezone
from sqlalchemy import create_engine, select, String, JSON, DateTime, ForeignKey, Integer
from sqlalchemy.orm import Session, DeclarativeBase, mapped_column, validates, relationship, Mapped, sessionmaker, reconstructor


class Base(DeclarativeBase):
  pass


engine = create_engine('sqlite:///data/app/chats.db')
