import { Component, effect, ElementRef, inject, input } from '@angular/core';
import {
  ArrowDown, ArrowUp, CalendarDays, Check, CheckCheck, ChevronLeft, ChevronRight,
  Circle, CircleCheck, Clock3, ListTodo, LoaderCircle, Plus, RotateCw, Search,
  SlidersHorizontal, Trash2, X, Target, Pencil, Eye, EyeOff, ArrowRight, LogOut, LogIn, CloudUpload, Share2, UserPlus, createElement,
} from 'lucide';

const icons = {
  ArrowDown, ArrowUp, CalendarDays, Check, CheckCheck, ChevronLeft, ChevronRight,
  Circle, CircleCheck, Clock3, ListTodo, LoaderCircle, Plus, RotateCw, Search,
  SlidersHorizontal, Trash2, X, Target, Pencil, Eye, EyeOff, ArrowRight, LogOut, LogIn, CloudUpload, Share2, UserPlus,
};

@Component({
  selector: 'app-icon',
  template: '',
  host: { 'aria-hidden': 'true' },
})
export class Icon {
  readonly name = input.required<keyof typeof icons>();
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    effect(() => {
      this.element.nativeElement.replaceChildren(
        createElement(icons[this.name()], { width: 18, height: 18, 'stroke-width': 1.6 }),
      );
    });
  }
}