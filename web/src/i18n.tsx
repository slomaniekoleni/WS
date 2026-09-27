import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type Lang = 'en' | 'ru'

const en = {
  'nav.services': 'Services',
  'nav.artists': 'Artists',
  'nav.info': 'Info',
  'nav.book': 'Book',

  'home.tagline': 'Tattoo & piercing studio in Minsk',
  'home.h1.1': 'Make a wish.',
  'home.h1.2': "We'll paint",
  'home.h1.3': 'the second eye.',
  'home.lead': "Three tattoo artists, two piercers, one room where every detail matters. Come with an idea, leave with work you'll be glad to wear for life.",
  'home.cta.works': 'See our work',
  'home.scroll': 'scroll',
  'home.worksHint': 'Keep scrolling: the gallery moves sideways',
  'home.worksSwipe': 'Swipe to see more',
  'home.piercingStyle': 'piercing',
  'home.allArtists': 'All artists and their work',
  'home.cta.book': 'Book a visit',
  'home.cta.services': 'Prices',
  'home.tattoo.title': 'Tattoo',
  'home.tattoo.text': 'Starts with a free consultation: we talk through your idea, size and placement, then book the session.',
  'home.piercing.title': 'Piercing',
  'home.piercing.text': 'Book online in a minute. Sterile, titanium jewelry, clear aftercare.',
  'home.age.title': '16+',
  'home.age.text': '16–17 with a parent or guardian, 18+ on your own. Bring your ID.',
  'home.artists': 'Our artists',
  'home.works': 'Our work',
  'home.visit': 'Visit us',

  'services.title': 'Services and prices',
  'services.tattoo': 'Tattoo',
  'services.piercing': 'Piercing',
  'services.free': 'free',
  'services.from': 'from',
  'services.min': 'min',
  'services.h': 'h',
  'services.book': 'Book',
  'services.afterConsultation': 'booked after a consultation',
  'services.adultsOnly': '18+',
  'services.note': 'Tattoo prices depend on size, detail and placement; the artist gives the exact price at the consultation.',

  'artists.title': 'Artists',
  'artists.tattoo': 'Tattoo artist',
  'artists.piercing': 'Piercer',
  'artists.book': 'Book with {name}',

  'info.title': 'Good to know',
  'info.hours': 'Opening hours',
  'info.contacts': 'Contacts',

  'book.title': 'Book a visit',
  'book.step.service': 'What would you like?',
  'book.step.artist': 'Artist',
  'book.step.time': 'Date and time',
  'book.step.details': 'Your details',
  'book.anyArtist': 'Any artist',
  'book.noSlots': 'No free time in the next two weeks. Try another artist or write to us.',
  'book.pickDay': 'Pick a day',
  'book.loading': 'Loading…',
  'book.name': 'Name',
  'book.phone': 'Phone',
  'book.phone.invalid': 'Please enter a valid phone number, e.g. +375 29 123-45-67.',
  'book.email': 'Email (optional)',
  'book.age': 'Age',
  'book.age.adult': "I'm 18 or older",
  'book.age.minor': "I'm 16–17 and will come with a parent or guardian",
  'book.age.adultsOnlyNote': 'This procedure is 18+ only.',
  'book.notes': 'Anything we should know? (optional)',
  'book.tattoo.title': 'Tell us about your tattoo idea',
  'book.tattoo.idea': 'Idea',
  'book.tattoo.placement': 'Placement',
  'book.tattoo.size': 'Approximate size',
  'book.tattoo.style': 'Style (if you know)',
  'book.tattoo.refs': 'You can bring or send reference images before the consultation.',
  'book.consent': 'I agree to the processing of my personal data for this booking.',
  'book.submit': 'Send request',
  'book.sending': 'Sending…',
  'book.summary': '{service} with {artist}, {when}',
  'book.change': 'change',
  'book.done.title': 'Request sent!',
  'book.done.text': '{artist} will confirm your booking shortly and we will contact you. See you at Wise City!',
  'book.done.again': 'Book something else',
  'book.error.taken': 'Someone just took this time. Please pick another one.',
  'book.error.generic': 'Something went wrong. Please try again or contact us.',

  'chat.open': 'Ask us',
  'chat.close': 'Close chat',
  'chat.title': 'Wise City',
  'chat.subtitle': 'AI assistant · prices, aftercare, booking',
  'chat.greeting': 'Hi! I can tell you about prices, artists and aftercare, or book you in. What are you thinking of?',
  'chat.placeholder': 'Type a message…',
  'chat.send': 'Send',
  'chat.typing': 'Typing',
  'chat.tooMany': 'Too many messages. Please wait a minute.',
  'chat.unavailable': 'The chat is unavailable right now. Please call or message us.',
  'chat.error': 'Message not sent. Please try again.',

  'footer.rights': 'Tattoo & piercing, Minsk',
  'common.error': 'Could not load data. Please refresh the page.',
  'notFound.title': 'Page not found',
  'notFound.text': 'This page does not exist or has moved.',
  'notFound.home': 'To the home page',
}

export type Key = keyof typeof en

const ru: Record<Key, string> = {
  'nav.services': 'Услуги',
  'nav.artists': 'Мастера',
  'nav.info': 'Инфо',
  'nav.book': 'Записаться',

  'home.tagline': 'Тату и пирсинг студия в Минске',
  'home.h1.1': 'Загадай.',
  'home.h1.2': 'Мы нарисуем',
  'home.h1.3': 'второй глаз.',
  'home.lead': 'Три тату-мастера, два пирсера и одна комната, где важна каждая деталь. Приходите с идеей — уходите с работой, которую не стыдно носить всю жизнь.',
  'home.cta.works': 'Смотреть работы',
  'home.scroll': 'листайте',
  'home.worksHint': 'Листайте дальше — галерея едет вбок',
  'home.worksSwipe': 'Смахните, чтобы увидеть больше',
  'home.piercingStyle': 'пирсинг',
  'home.allArtists': 'Все мастера и их работы',
  'home.cta.book': 'Записаться',
  'home.cta.services': 'Цены',
  'home.tattoo.title': 'Тату',
  'home.tattoo.text': 'Начинаем с бесплатной консультации: обсуждаем идею, размер и место, потом записываем на сеанс.',
  'home.piercing.title': 'Пирсинг',
  'home.piercing.text': 'Запись онлайн за минуту. Стерильно, титановые украшения, понятный уход.',
  'home.age.title': '16+',
  'home.age.text': '16–17 лет с родителем или законным представителем, с 18 самостоятельно. Возьмите документ.',
  'home.artists': 'Наши мастера',
  'home.works': 'Наши работы',
  'home.visit': 'Как нас найти',

  'services.title': 'Услуги и цены',
  'services.tattoo': 'Тату',
  'services.piercing': 'Пирсинг',
  'services.free': 'бесплатно',
  'services.from': 'от',
  'services.min': 'мин',
  'services.h': 'ч',
  'services.book': 'Записаться',
  'services.afterConsultation': 'запись после консультации',
  'services.adultsOnly': '18+',
  'services.note': 'Цена тату зависит от размера, детализации и места; точную стоимость мастер назовёт на консультации.',

  'artists.title': 'Мастера',
  'artists.tattoo': 'Тату-мастер',
  'artists.piercing': 'Пирсер',
  'artists.book': 'Записаться к {name}',

  'info.title': 'Полезно знать',
  'info.hours': 'Часы работы',
  'info.contacts': 'Контакты',

  'book.title': 'Запись',
  'book.step.service': 'Что хотите сделать?',
  'book.step.artist': 'Мастер',
  'book.step.time': 'Дата и время',
  'book.step.details': 'Ваши данные',
  'book.anyArtist': 'Любой мастер',
  'book.noSlots': 'В ближайшие две недели нет свободного времени. Попробуйте другого мастера или напишите нам.',
  'book.pickDay': 'Выберите день',
  'book.loading': 'Загрузка…',
  'book.name': 'Имя',
  'book.phone': 'Телефон',
  'book.phone.invalid': 'Введите корректный номер, например +375 29 123-45-67.',
  'book.email': 'Email (необязательно)',
  'book.age': 'Возраст',
  'book.age.adult': 'Мне есть 18',
  'book.age.minor': 'Мне 16–17, приду с родителем или законным представителем',
  'book.age.adultsOnlyNote': 'Эта процедура только 18+.',
  'book.notes': 'Что нам стоит знать? (необязательно)',
  'book.tattoo.title': 'Расскажите об идее тату',
  'book.tattoo.idea': 'Идея',
  'book.tattoo.placement': 'Место',
  'book.tattoo.size': 'Примерный размер',
  'book.tattoo.style': 'Стиль (если знаете)',
  'book.tattoo.refs': 'Референсы можно принести или прислать до консультации.',
  'book.consent': 'Я согласен(на) на обработку персональных данных для этой записи.',
  'book.submit': 'Отправить заявку',
  'book.sending': 'Отправляем…',
  'book.summary': '{service}, мастер {artist}, {when}',
  'book.change': 'изменить',
  'book.done.title': 'Заявка отправлена!',
  'book.done.text': '{artist} скоро подтвердит запись, и мы с вами свяжемся. До встречи в Wise City!',
  'book.done.again': 'Записаться ещё',
  'book.error.taken': 'Это время только что заняли. Выберите, пожалуйста, другое.',
  'book.error.generic': 'Что-то пошло не так. Попробуйте ещё раз или свяжитесь с нами.',

  'chat.open': 'Написать нам',
  'chat.close': 'Закрыть чат',
  'chat.title': 'Wise City',
  'chat.subtitle': 'AI-ассистент · цены, уход, запись',
  'chat.greeting': 'Привет! Расскажу о ценах, мастерах и уходе или запишу вас. Что планируете?',
  'chat.placeholder': 'Напишите сообщение…',
  'chat.send': 'Отправить',
  'chat.typing': 'Печатает',
  'chat.tooMany': 'Слишком много сообщений. Подождите минуту.',
  'chat.unavailable': 'Чат сейчас недоступен. Позвоните или напишите нам.',
  'chat.error': 'Сообщение не отправилось. Попробуйте ещё раз.',

  'footer.rights': 'Тату и пирсинг, Минск',
  'common.error': 'Не удалось загрузить данные. Обновите страницу.',
  'notFound.title': 'Страница не найдена',
  'notFound.text': 'Такой страницы нет или она переехала.',
  'notFound.home': 'На главную',
}

const dictionaries: Record<Lang, Record<Key, string>> = { en, ru }

const styleNames: Record<string, [en: string, ru: string]> = {
  'fine-line': ['fine line', 'тонкие линии'],
  minimalism: ['minimalism', 'минимализм'],
  lettering: ['lettering', 'надписи'],
  botanical: ['botanical', 'ботаника'],
  realism: ['realism', 'реализм'],
  'black-and-grey': ['black & grey', 'чёрно-серое'],
  portrait: ['portrait', 'портреты'],
  traditional: ['traditional', 'олдскул'],
  'neo-traditional': ['neo-traditional', 'нео-традишнл'],
  blackwork: ['blackwork', 'блэкворк'],
  color: ['color', 'цвет'],
  piercing: ['piercing', 'пирсинг'],
}

interface I18n {
  lang: Lang
  setLang: (lang: Lang) => void
  t: (key: Key, vars?: Record<string, string>) => string
  styleName: (style: string) => string
  locale: string
}

const I18nContext = createContext<I18n | null>(null)

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem('lang')
    if (saved === 'en' || saved === 'ru') return saved
  } catch {
    /* storage unavailable */
  }
  return /^(ru|be|uk)/i.test(navigator.language) ? 'ru' : 'en'
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(initialLang)

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  const setLang = (next: Lang) => {
    setLangState(next)
    try {
      localStorage.setItem('lang', next)
    } catch {
      /* storage unavailable */
    }
  }

  const t = (key: Key, vars?: Record<string, string>) =>
    dictionaries[lang][key].replace(/\{(\w+)\}/g, (_, name: string) => vars?.[name] ?? '')

  const styleName = (style: string) => styleNames[style]?.[lang === 'ru' ? 1 : 0] ?? style

  return (
    <I18nContext.Provider value={{ lang, setLang, t, styleName, locale: lang === 'ru' ? 'ru-RU' : 'en-GB' }}>
      {children}
    </I18nContext.Provider>
  )
}

// eslint-disable-next-line react/only-export-components
export function useI18n() {
  const ctx = useContext(I18nContext)
  if (!ctx) throw new Error('useI18n outside I18nProvider')
  return ctx
}
