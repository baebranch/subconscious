import json

from src.utilities.dictobj import DictObj


class LangLoader(DictObj):
  """ Load the language file
      :param lang: The language to load
      :param loc: The location to load the language from
  """
  def __init__(self, lang: str, loc: str=None) -> None:
    """ Load the language file """
    with open(f'./src/lang/{lang}.json', 'r', encoding='utf-8') as f:
      super().__init__(DictObj(json.load(f)))

  def load_lang(self, lang: str, loc: str=None) -> None:
    # TODO: Add support for locations
    # Load the language file
    with open(f'./src/lang/{lang}.json', 'r', encoding='utf-8') as f:
      super().__init__(DictObj(json.load(f)))
    
    # Reload the UI with the new language
    self.trigger()
  