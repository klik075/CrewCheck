package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import org.springframework.objenesis.Objenesis;
import org.springframework.objenesis.ObjenesisStd;

import java.lang.reflect.Field;
import java.lang.reflect.Modifier;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.HashSet;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 저장할 때와 꺼낼 때 Game을 깊은 복사하는 테스트용 저장소. (Redis 저장소 흉내)
 *
 * InMemoryGameRepository는 같은 객체를 돌려주므로, 꺼낸 Game을 고치고 save()를 빼먹어도 티가 나지 않는다.
 * 이 저장소는 save() 시점의 상태만 남기므로, save() 없이 바꾼 내용은 다음 findById()에서 사라진다.
 * → save 누락이 테스트 실패로 드러난다.
 *
 * 복사 규칙
 *  - Game, GamePlayer (game.entity 패키지의 일반 클래스): 필드 하나하나 깊은 복사
 *  - Map / Set / List: 새 컬렉션에 원소를 깊은 복사해서 담는다 (원래 종류 유지)
 *  - record, enum, String, 숫자, java.time: 바뀌지 않는 값이라 그대로 공유
 *  - 그 밖의 타입: 복사 방법을 모르므로 예외. (Game에 새 가변 필드 타입이 생기면 여기서 바로 알 수 있다)
 */
public class CopyingGameRepository implements GameRepository {

    private static final String ENTITY_PACKAGE = Game.class.getPackageName();
    private static final Objenesis OBJENESIS = new ObjenesisStd(true);

    private final Map<String, Game> store = new ConcurrentHashMap<>();
    private int saveCount;

    @Override
    public Game save(Game game) {
        store.put(game.getGameId(), deepCopy(game));
        saveCount++;
        return game;
    }

    @Override
    public Optional<Game> findById(String gameId) {
        return Optional.ofNullable(store.get(gameId)).map(CopyingGameRepository::deepCopy);
    }

    @Override
    public List<Game> findAll() {
        return store.values().stream().map(CopyingGameRepository::deepCopy).toList();
    }

    @Override
    public void delete(String gameId) {
        store.remove(gameId);
    }

    /** 지금까지 save()가 불린 횟수 */
    public int saveCount() {
        return saveCount;
    }

    // ---------- 깊은 복사 ----------

    public static <T> T deepCopy(T value) {
        @SuppressWarnings("unchecked")
        T copy = (T) copy(value, new IdentityHashMap<>());
        return copy;
    }

    private static Object copy(Object value, IdentityHashMap<Object, Object> copied) {
        if (value == null || isImmutable(value.getClass())) {
            return value;
        }
        Object done = copied.get(value);
        if (done != null) {
            return done;
        }
        if (value instanceof Map<?, ?> map) {
            return copyMap(map, copied);
        }
        if (value instanceof Set<?> set) {
            Collection<Object> target = null; // null = 고칠 수 없는 Set (Set.of 등)
            if (set instanceof LinkedHashSet) {
                target = new LinkedHashSet<>();
            } else if (set instanceof HashSet) {
                target = new HashSet<>();
            }
            return copyCollection(set, copied, target, true);
        }
        if (value instanceof List<?> list) {
            Collection<Object> target = list instanceof ArrayList ? new ArrayList<>() : null; // null = List.of 등
            return copyCollection(list, copied, target, false);
        }
        if (value.getClass().getPackageName().equals(ENTITY_PACKAGE)) {
            return copyFields(value, copied);
        }
        throw new IllegalStateException("깊은 복사 방법을 모르는 타입입니다: " + value.getClass().getName()
                + " (CopyingGameRepository에 복사 규칙을 추가하세요)");
    }

    private static boolean isImmutable(Class<?> type) {
        return type.isEnum() || type.isRecord()
                || (type.getSuperclass() != null && type.getSuperclass().isEnum()) // 몸체가 있는 enum 상수
                || type == String.class || type == Boolean.class || type == Character.class
                || Number.class.isAssignableFrom(type) && type.getName().startsWith("java.lang.")
                || type.getPackageName().equals("java.time");
    }

    private static Object copyFields(Object source, IdentityHashMap<Object, Object> copied) {
        Object target = OBJENESIS.newInstance(source.getClass());
        copied.put(source, target);
        for (Class<?> c = source.getClass(); c != Object.class; c = c.getSuperclass()) {
            for (Field field : c.getDeclaredFields()) {
                if (Modifier.isStatic(field.getModifiers())) {
                    continue;
                }
                field.setAccessible(true);
                try {
                    field.set(target, copy(field.get(source), copied));
                } catch (IllegalAccessException e) {
                    throw new IllegalStateException("필드 복사 실패: " + c.getSimpleName() + "." + field.getName(), e);
                }
            }
        }
        return target;
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static Object copyMap(Map<?, ?> source, IdentityHashMap<Object, Object> copied) {
        Map<Object, Object> target;
        boolean mutable = true;
        if (source instanceof EnumMap<?, ?> enumMap) {
            target = (Map<Object, Object>) ((EnumMap) enumMap).clone();
            target.clear();
        } else if (source instanceof LinkedHashMap) {
            target = new LinkedHashMap<>();
        } else if (source instanceof HashMap) {
            target = new HashMap<>();
        } else if (source instanceof ConcurrentHashMap) {
            target = new ConcurrentHashMap<>();
        } else {
            target = new LinkedHashMap<>(); // Map.of, Collections.unmodifiableMap 등 (순서 유지)
            mutable = false;
        }
        copied.put(source, target);
        for (Map.Entry<?, ?> e : source.entrySet()) {
            target.put(copy(e.getKey(), copied), copy(e.getValue(), copied));
        }
        return mutable ? target : Collections.unmodifiableMap(target);
    }

    @SuppressWarnings("unchecked")
    private static Object copyCollection(Collection<?> source, IdentityHashMap<Object, Object> copied,
                                         Collection<Object> target, boolean isSet) {
        boolean mutable = target != null;
        Collection<Object> into = mutable ? target : (isSet ? new LinkedHashSet<>() : new ArrayList<>());
        copied.put(source, into);
        for (Object element : source) {
            into.add(copy(element, copied));
        }
        if (mutable) {
            return into;
        }
        return isSet ? Collections.unmodifiableSet((Set<Object>) into) : Collections.unmodifiableList((List<Object>) into);
    }
}
