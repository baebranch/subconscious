import logging

# Logging setup
logging.basicConfig(format='[%(levelname)s|%(asctime)s.%(msecs)04d|%(filename)s|%(lineno)d] %(message)s', datefmt='%Y-%m-%d %H:%M:%S')

# Subconscious logger
s_logger = logging.getLogger('subconscious')
logger.setLevel(os.getenv('LOG_LEVEL', 'DEBUG'))

# Thoughts logger
t_logger = logging.getLogger('thoughts')
logger.setLevel(os.getenv('LOG_LEVEL', 'DEBUG'))
