export function readInputValue(element) {
  return element && typeof element.value === 'string' ? element.value : null;
}
