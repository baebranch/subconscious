""" Holds data manipulation classes """


class DictObj(dict):
  """ Converts a dictionary to an object while preserving the dict properties """
  def __init__(self, *args, **kwargs):
    super().__init__(*args, **kwargs)

    for a, b in args[0].items():
      try:
        if isinstance(b, (list, tuple)):
          setattr(self, a, [DictObj(x) if isinstance(x, dict) else x for x in b])
        elif not isinstance(a, (tuple)):
          setattr(self, a, DictObj(b) if isinstance(b, dict) else b)
      except TypeError:
        if isinstance(b, (list, tuple)):
          setattr(self, str(a), [DictObj(x) if isinstance(x, dict) else x for x in b])
        elif not isinstance(a, (tuple)):
          setattr(self, str(a), DictObj(b) if isinstance(b, dict) else b)
